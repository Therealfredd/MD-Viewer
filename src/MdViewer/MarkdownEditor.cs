using static MdViewer.NativeMethods;

namespace MdViewer;

/// <summary>
/// Plain-text source editor built on the Windows RichEdit control in plain-text mode:
/// multi-level undo/redo, fast with large files, and pasting never brings in formatting.
/// </summary>
internal sealed class MarkdownEditor : RichTextBox
{
    /// <summary>Raised when the visible portion of the text may have changed (scroll, wheel, keys).</summary>
    public event EventHandler? ViewportChanged;

    public MarkdownEditor()
    {
        BorderStyle = BorderStyle.None;
        AcceptsTab = true;
        DetectUrls = false;
        HideSelection = false;
        EnableAutoDragDrop = false;
        ScrollBars = RichTextBoxScrollBars.Both;
        LanguageOption = RichTextBoxLanguageOptions.UIFonts | RichTextBoxLanguageOptions.DualFont;
        ShortcutsEnabled = true;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Must be sent while the control is empty.
        SendMessage(Handle, EM_SETTEXTMODE, TM_PLAINTEXT | TM_MULTILEVELUNDO | TM_MULTICODEPAGE, IntPtr.Zero);
        AutoWordSelection = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        // Let the form's menu shortcuts win first.
        if (base.ProcessCmdKey(ref msg, keyData))
            return true;

        switch (keyData)
        {
            case Keys.Control | Keys.Shift | Keys.Z:
                Redo();
                return true;

            // Swallow RichEdit's built-in formatting shortcuts; this is a plain-text editor.
            case Keys.Control | Keys.B:
            case Keys.Control | Keys.I:
            case Keys.Control | Keys.U:
            case Keys.Control | Keys.L:
            case Keys.Control | Keys.R:
            case Keys.Control | Keys.E:
            case Keys.Control | Keys.J:
            case Keys.Control | Keys.D1:
            case Keys.Control | Keys.D2:
            case Keys.Control | Keys.D5:
            case Keys.Control | Keys.Oemplus:
            case Keys.Control | Keys.Shift | Keys.Oemplus:
            case Keys.Control | Keys.Shift | Keys.A:
            case Keys.Control | Keys.Shift | Keys.L:
            case Keys.Control | Keys.Shift | Keys.Oemcomma:
            case Keys.Control | Keys.Shift | Keys.OemPeriod:
                return true;
        }
        return false;
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        if (m.Msg is WM_VSCROLL or WM_MOUSEWHEEL or WM_KEYUP)
            ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnVScroll(EventArgs e)
    {
        base.OnVScroll(e);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        ViewportChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Index of the first character visible at the top of the editor.</summary>
    public int FirstVisibleCharIndex => GetCharIndexFromPosition(new Point(2, 2));
}
