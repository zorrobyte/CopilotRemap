using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CopilotRemap;

/// <summary>
/// Spotlight-style overlay for quickly starting or resuming Claude sessions.
/// Two modes:
///   Main   — type a prompt → Enter → new Claude chat; or pick Resume/Continue/Desktop/Web
///   Resume — full-text search through past conversations, arrow-select, Enter to open
/// </summary>
public sealed class QuickLaunchWindow : Form
{
    public record LaunchItem(string Title, string Subtitle, Action<string> Execute);

    private const int WindowWidth = 640;
    private const int SearchAreaHeight = 56;
    private const int ItemHeight = 44;
    private const int MaxVisibleItems = 8;

    private readonly TextBox _searchBox;

    // Mode: Main menu vs Resume search
    private enum Mode { Main, Resume }
    private Mode _mode = Mode.Main;

    // Main mode
    private readonly List<LaunchItem> _mainItems;
    private readonly Action<string>? _askAction;

    // Resume mode
    private List<ChatSessionScanner.ChatSession>? _allSessions;
    private readonly Action<string>? _resumeByIdAction;

    // Shared display state
    private List<LaunchItem> _visibleItems = [];
    private int _selectedIndex;
    private int _scrollOffset;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public QuickLaunchWindow(
        List<LaunchItem> mainItems,
        Action<string>? askAction = null,
        Action<string>? resumeByIdAction = null)
    {
        _mainItems = mainItems;
        _askAction = askAction;
        _resumeByIdAction = resumeByIdAction;

        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        DoubleBuffered = true;
        BackColor = Color.FromArgb(30, 30, 30);

        _searchBox = new TextBox
        {
            Font = new Font("Segoe UI", 16f),
            ForeColor = Color.FromArgb(220, 220, 220),
            BackColor = Color.FromArgb(30, 30, 30),
            BorderStyle = BorderStyle.None,
            Bounds = new Rectangle(44, 16, WindowWidth - 60, 28),
            PlaceholderText = "Ask Claude something…"
        };
        _searchBox.TextChanged += (_, _) => RefreshItems();
        _searchBox.KeyDown += OnInputKeyDown;

        Controls.Add(_searchBox);
        SetMainMode();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        try
        {
            int pref = 2; // DWMWCP_ROUND — native rounded corners on Windows 11
            DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
        }
        catch { }
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        _searchBox.Focus();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        Close();
    }

    // --- Layout ---

    private void RecalcSizeAndCenter()
    {
        int displayCount = Math.Min(_visibleItems.Count, MaxVisibleItems);
        int h = SearchAreaHeight + displayCount * ItemHeight + (displayCount > 0 ? 4 : 0);
        ClientSize = new Size(WindowWidth, h);

        // Center on the screen where the cursor is
        var screen = Screen.FromPoint(Cursor.Position);
        var wa = screen.WorkingArea;
        int x = wa.X + (wa.Width - Width) / 2;
        int y = wa.Y + (wa.Height - Height) / 2;
        Location = new Point(x, y);
    }

    // --- Mode switching ---

    private void SetMainMode()
    {
        _mode = Mode.Main;
        _searchBox.PlaceholderText = "Ask Claude something…";
        _searchBox.Text = "";
        _selectedIndex = 0;
        _scrollOffset = 0;
        RefreshItems();
    }

    private void SetResumeMode()
    {
        _mode = Mode.Resume;
        _allSessions ??= ChatSessionScanner.Scan();
        _searchBox.PlaceholderText = "Search conversations…";
        _searchBox.Text = "";
        _selectedIndex = 0;
        _scrollOffset = 0;
        RefreshItems();
    }

    // --- Filtering ---

    private void RefreshItems()
    {
        var query = _searchBox.Text.Trim();
        _visibleItems = [];

        if (_mode == Mode.Main)
            BuildMainList(query);
        else
            BuildResumeList(query);

        _selectedIndex = Math.Clamp(_selectedIndex, 0, Math.Max(0, _visibleItems.Count - 1));
        EnsureSelectionVisible();
        RecalcSizeAndCenter();
        Invalidate();
    }

    private void BuildMainList(string query)
    {
        // If user typed text, the default action is "Ask Claude" (new chat with that prompt)
        if (!string.IsNullOrEmpty(query) && _askAction != null)
        {
            var display = query.Length > 55 ? query[..55] + "…" : query;
            _visibleItems.Add(new LaunchItem(
                $"Ask Claude: \"{display}\"",
                "New Claude Code chat – prompt copied to clipboard",
                _askAction));
        }

        var filtered = string.IsNullOrEmpty(query)
            ? _mainItems
            : _mainItems.Where(i =>
                i.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                i.Subtitle.Contains(query, StringComparison.OrdinalIgnoreCase));
        _visibleItems.AddRange(filtered);
    }

    private void BuildResumeList(string query)
    {
        if (_allSessions == null) return;

        var matches = string.IsNullOrEmpty(query)
            ? _allSessions
            : _allSessions.Where(s =>
                s.SearchableText.Contains(query, StringComparison.OrdinalIgnoreCase));

        foreach (var s in matches)
        {
            var title = Truncate(s.FirstMessage.ReplaceLineEndings(" "), 70);
            var age = FormatAge(s.LastModified);
            var subtitle = $"{s.Project}  ·  {age}";
            var id = s.SessionId; // capture for lambda
            _visibleItems.Add(new LaunchItem(title, subtitle, _ => _resumeByIdAction?.Invoke(id)));
        }
    }

    // --- Keyboard ---

    private void OnInputKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Escape:
                if (_mode == Mode.Resume)
                {
                    SetMainMode();
                    e.Handled = true;
                }
                else
                {
                    Close();
                    e.Handled = true;
                }
                break;

            case Keys.Down:
                if (_visibleItems.Count > 0)
                {
                    _selectedIndex = (_selectedIndex + 1) % _visibleItems.Count;
                    EnsureSelectionVisible();
                }
                Invalidate();
                e.Handled = true;
                break;

            case Keys.Up:
                if (_visibleItems.Count > 0)
                {
                    _selectedIndex = (_selectedIndex - 1 + _visibleItems.Count) % _visibleItems.Count;
                    EnsureSelectionVisible();
                }
                Invalidate();
                e.Handled = true;
                break;

            case Keys.Enter:
                ExecuteSelected();
                e.Handled = true;
                e.SuppressKeyPress = true;
                break;
        }
    }

    private void EnsureSelectionVisible()
    {
        if (_selectedIndex < _scrollOffset)
            _scrollOffset = _selectedIndex;
        else if (_selectedIndex >= _scrollOffset + MaxVisibleItems)
            _scrollOffset = _selectedIndex - MaxVisibleItems + 1;
    }

    private void ExecuteSelected()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _visibleItems.Count) return;
        var item = _visibleItems[_selectedIndex];

        // Special: if it's the "Resume Chat…" item in main mode, switch to resume mode
        if (_mode == Mode.Main && item.Title == "Resume Chat…")
        {
            SetResumeMode();
            return;
        }

        var query = _searchBox.Text.Trim();
        Close();
        item.Execute(query);
    }

    // --- Custom painting ---

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        // Search icon
        DrawSearchIcon(g, 14, 18);

        // Mode badge
        if (_mode == Mode.Resume)
        {
            using var badgeFont = new Font("Segoe UI", 8f);
            using var badgeBrush = new SolidBrush(Color.FromArgb(80, 160, 255));
            g.DrawString("RESUME", badgeFont, badgeBrush, Width - 70, 22);
        }

        // Separator
        int displayCount = Math.Min(_visibleItems.Count, MaxVisibleItems);
        if (displayCount > 0)
        {
            using var sepPen = new Pen(Color.FromArgb(50, 50, 50));
            g.DrawLine(sepPen, 12, SearchAreaHeight - 1, Width - 12, SearchAreaHeight - 1);
        }

        // Items
        using var titleFont = new Font("Segoe UI", 11f);
        using var subtitleFont = new Font("Segoe UI", 8.25f);
        using var titleBrush = new SolidBrush(Color.FromArgb(225, 225, 225));
        using var dimBrush = new SolidBrush(Color.FromArgb(120, 120, 120));
        using var selBrush = new SolidBrush(Color.FromArgb(50, 80, 160, 255));

        for (int vi = 0; vi < displayCount; vi++)
        {
            int dataIdx = _scrollOffset + vi;
            if (dataIdx >= _visibleItems.Count) break;

            int y = SearchAreaHeight + vi * ItemHeight;

            if (dataIdx == _selectedIndex)
            {
                using var rr = RoundedRect(new Rectangle(6, y + 2, Width - 12, ItemHeight - 4), 6);
                g.FillPath(selBrush, rr);
            }

            g.DrawString(_visibleItems[dataIdx].Title, titleFont, titleBrush, 18, y + 4);
            g.DrawString(_visibleItems[dataIdx].Subtitle, subtitleFont, dimBrush, 18, y + 24);
        }

        // Scroll indicators
        if (_scrollOffset > 0)
        {
            using var arrowBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
            g.DrawString("▲", subtitleFont, arrowBrush, Width - 24, SearchAreaHeight + 2);
        }
        if (_scrollOffset + MaxVisibleItems < _visibleItems.Count)
        {
            using var arrowBrush = new SolidBrush(Color.FromArgb(100, 100, 100));
            int bottomY = SearchAreaHeight + displayCount * ItemHeight - 14;
            g.DrawString("▼", subtitleFont, arrowBrush, Width - 24, bottomY);
        }

        // Border
        using var borderPen = new Pen(Color.FromArgb(50, 50, 50));
        g.DrawRectangle(borderPen, 0, 0, Width - 1, Height - 1);
    }

    private static void DrawSearchIcon(Graphics g, int x, int y)
    {
        using var pen = new Pen(Color.FromArgb(120, 120, 120), 2f);
        int r = 7;
        g.DrawEllipse(pen, x, y, r * 2, r * 2);
        float offset = r * 0.7f;
        g.DrawLine(pen, x + r + offset, y + r + offset, x + r + offset + 5, y + r + offset + 5);
    }

    // --- Mouse ---

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int vi = (e.Y - SearchAreaHeight) / ItemHeight;
        int dataIdx = _scrollOffset + vi;
        if (dataIdx >= 0 && dataIdx < _visibleItems.Count && dataIdx != _selectedIndex)
        {
            _selectedIndex = dataIdx;
            Invalidate();
        }
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int vi = (e.Y - SearchAreaHeight) / ItemHeight;
        int dataIdx = _scrollOffset + vi;
        if (dataIdx >= 0 && dataIdx < _visibleItems.Count)
        {
            _selectedIndex = dataIdx;
            ExecuteSelected();
        }
    }

    // --- Helpers ---

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";

    private static string FormatAge(DateTime dt)
    {
        var span = DateTime.Now - dt;
        if (span.TotalMinutes < 1) return "just now";
        if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
        if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
        if (span.TotalDays < 30) return $"{(int)span.TotalDays}d ago";
        return dt.ToString("dd MMM yyyy");
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int d = radius * 2;
        path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
