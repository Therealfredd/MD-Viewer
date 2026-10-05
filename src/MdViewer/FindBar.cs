namespace MdViewer;

/// <summary>A slim find strip shown under the menu (Ctrl+F). Enter = next, Shift+Enter = previous, Esc = close.</summary>
internal sealed class FindBar : ToolStrip
{
    private readonly ToolStripTextBox query = new() { AutoSize = false };
    private readonly ToolStripButton matchCase = new("Aa") { CheckOnClick = true, ToolTipText = "Match case" };
    private readonly ToolStripLabel result = new();

    /// <summary>Argument is true when searching backwards.</summary>
    public event Action<bool>? FindRequested;
    public event EventHandler? CloseRequested;

    public FindBar()
    {
        GripStyle = ToolStripGripStyle.Hidden;
        Dock = DockStyle.Top;
        Padding = new Padding(6, 2, 6, 2);
        CanOverflow = false;
        Stretch = true;

        var prev = new ToolStripButton("Previous") { ToolTipText = "Find previous (Shift+F3)" };
        var next = new ToolStripButton("Next") { ToolTipText = "Find next (F3)" };
        var close = new ToolStripButton("✕") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Close (Esc)" };
        prev.Click += (_, _) => FindRequested?.Invoke(true);
        next.Click += (_, _) => FindRequested?.Invoke(false);
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);

        query.KeyDown += OnQueryKeyDown;
        query.TextChanged += (_, _) => result.Text = "";

        Items.AddRange(new ToolStripItem[]
        {
            new ToolStripLabel("Find:"), query, next, prev, matchCase, result, close,
        });
    }

    public string Query => query.Text;
    public bool MatchCase => matchCase.Checked;

    public void SetResult(bool found) => result.Text = found ? "" : "Not found";

    public void FocusQuery(string? initial)
    {
        if (!string.IsNullOrEmpty(initial) && !initial.Contains('\n'))
            query.Text = initial;
        query.Focus();
        query.SelectAll();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        query.Width = LogicalToDeviceUnits(260);
        base.OnLayout(e);
    }

    protected override bool ProcessCmdKey(ref Message m, Keys keyData)
    {
        // ToolStrip swallows Esc for its own keyboard-navigation mode; use it to close instead.
        if (keyData == Keys.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            return true;
        }
        return base.ProcessCmdKey(ref m, keyData);
    }

    private void OnQueryKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter)
        {
            FindRequested?.Invoke(e.Shift);
            e.SuppressKeyPress = true;
        }
        else if (e.KeyCode == Keys.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.SuppressKeyPress = true;
        }
    }
}
