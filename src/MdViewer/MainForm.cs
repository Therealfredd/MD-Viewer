using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace MdViewer;

internal sealed class MainForm : Form
{
    private const string AppName = "MD Viewer";
    private const string AppHost = "app.mdviewer";
    private const string AppRoot = "https://" + AppHost + "/";

    private const string FileFilter =
        "Markdown files (*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdwn)|*.md;*.markdown;*.mdown;*.mkd;*.mkdn;*.mdwn|" +
        "Text files (*.txt)|*.txt|All files (*.*)|*.*";

    private static readonly HashSet<string> MarkdownExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".md", ".markdown", ".mdown", ".mkd", ".mkdn", ".mdwn", ".mdtxt", ".mdtext" };

    // Local files the preview is allowed to load (images / media only).
    private static readonly Dictionary<string, string> MediaTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".gif"] = "image/gif",
        [".webp"] = "image/webp", [".svg"] = "image/svg+xml", [".bmp"] = "image/bmp", [".ico"] = "image/x-icon",
        [".avif"] = "image/avif", [".apng"] = "image/apng",
        [".mp4"] = "video/mp4", [".webm"] = "video/webm", [".ogg"] = "audio/ogg", [".mp3"] = "audio/mpeg", [".wav"] = "audio/wav",
    };

    // Never launched from a link, even with confirmation.
    private static readonly HashSet<string> BlockedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".ps1", ".psm1", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh", ".msi", ".msp",
        ".scr", ".pif", ".lnk", ".url", ".hta", ".cpl", ".msc", ".jar", ".reg", ".dll", ".sys", ".appref-ms",
        ".application", ".gadget", ".inf", ".ins", ".scf", ".settingcontent-ms", ".library-ms", ".appx", ".msix",
    };

    private static readonly JsonWriterOptions JsonOptions = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly AppSettings settings;
    private readonly string? initialPath;
    private readonly bool initialEdit;

    // UI
    private readonly MenuStrip menu = new();
    private readonly SplitContainer split = new();
    private readonly MarkdownEditor editor = new();
    private readonly WebView2 web = new();
    private readonly FindBar findBar = new();
    private readonly StatusStrip status = new();
    private readonly ToolStripStatusLabel fileLabel = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel stateLabel = new();
    private readonly ToolStripStatusLabel posLabel = new();
    private readonly ToolStripStatusLabel formatLabel = new();
    private readonly System.Windows.Forms.Timer renderTimer = new();
    private readonly System.Windows.Forms.Timer syncTimer = new() { Interval = 40 };
    private readonly System.Windows.Forms.Timer reloadTimer = new() { Interval = 300 };

    private ToolStripMenuItem editModeItem = null!, modeButton = null!, undoItem = null!, redoItem = null!;
    private ToolStripMenuItem cutItem = null!, pasteItem = null!, wordWrapItem = null!;
    private ToolStripMenuItem themeSystemItem = null!, themeLightItem = null!, themeDarkItem = null!;

    // Document state. While the editor has never been shown, docText is the source of truth
    // (keeps viewing large files cheap); afterwards the editor owns the text.
    private string? filePath;
    private string docText = "";
    private bool editorHasDoc;
    private string? editorTextCache;
    private bool dirty;
    private string savedText = ""; // content as last loaded/saved, to clear 'modified' after undo
    private bool suppressEditorEvents;
    private TextFormat format = TextFormat.Default;
    private DateTime lastWriteUtc;
    private FileSystemWatcher? watcher;
    private bool changedOnDisk;

    // View state
    private bool editMode;
    private bool webReady;
    private int renderVersion;
    private bool resetScrollPending = true;
    private string? anchorPending;
    private int lastSyncedLine = -1;
    private Palette palette = Palette.Light;

    public MainForm(string? path, bool edit)
    {
        settings = AppSettings.Load();
        initialPath = path;
        initialEdit = edit;

        Text = AppName;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        MinimumSize = new Size(420, 300);
        AllowDrop = true;
        using (var s = Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico"))
            if (s != null) Icon = new Icon(s);

        BuildMenu();
        BuildStatusBar();
        BuildEditor();

        web.Dock = DockStyle.Fill;
        web.AllowExternalDrop = true; // dropped files arrive as navigations, intercepted below

        split.Dock = DockStyle.Fill;
        split.Orientation = Orientation.Vertical;
        split.Panel1.Padding = new Padding(10, 6, 0, 0);
        split.Panel1.Controls.Add(editor);
        split.Panel2.Controls.Add(web);
        split.Panel1Collapsed = true;
        split.SplitterMoved += (_, _) =>
        {
            // Only remember user drags, not programmatic/resize-driven moves.
            if (editMode && split.Width > 0 && MouseButtons.HasFlag(MouseButtons.Left))
                settings.SplitRatio = Math.Clamp((double)split.SplitterDistance / split.Width, 0.1, 0.9);
        };

        findBar.Visible = false;
        findBar.FindRequested += backwards => FindNext(backwards);
        findBar.CloseRequested += (_, _) => HideFind();

        // Dock order: last added docks outermost.
        Controls.Add(split);
        Controls.Add(findBar);
        Controls.Add(status);
        Controls.Add(menu);
        MainMenuStrip = menu;

        foreach (Control c in new Control[] { this, split.Panel1, split.Panel2, editor })
        {
            c.AllowDrop = true;
            c.DragEnter += OnDragEnter;
            c.DragDrop += OnDragDrop;
        }

        renderTimer.Tick += (_, _) => RenderNow();
        syncTimer.Tick += (_, _) => { syncTimer.Stop(); SyncPreviewToEditor(); };
        reloadTimer.Tick += (_, _) => { reloadTimer.Stop(); CheckExternalChange(); };

        RestoreWindowBounds();
        SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
    }

    // ------------------------------------------------------------------ UI construction

    private void BuildMenu()
    {
        menu.Padding = new Padding(4, 2, 0, 2);

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.AddRange(new ToolStripItem[]
        {
            Item("&New", Keys.Control | Keys.N, (_, _) => NewDocument()),
            Item("&Open...", Keys.Control | Keys.O, (_, _) => OpenWithDialog()),
            Item("&Save", Keys.Control | Keys.S, (_, _) => Save()),
            Item("Save &As...", Keys.Control | Keys.Shift | Keys.S, (_, _) => SaveAs()),
            new ToolStripSeparator(),
            Item("Open Containing &Folder", Keys.None, (_, _) => OpenContainingFolder()),
            new ToolStripSeparator(),
            Item("E&xit", Keys.None, (_, _) => Close()),
        });

        var edit = new ToolStripMenuItem("&Edit");
        undoItem = Item("&Undo", Keys.None, (_, _) => { if (editor.CanUndo) editor.Undo(); }, "Ctrl+Z");
        redoItem = Item("&Redo", Keys.None, (_, _) => { if (editor.CanRedo) editor.Redo(); }, "Ctrl+Y");
        cutItem = Item("Cu&t", Keys.None, (_, _) => EditCommand("cut"), "Ctrl+X");
        var copyItem = Item("&Copy", Keys.None, (_, _) => EditCommand("copy"), "Ctrl+C");
        pasteItem = Item("&Paste", Keys.None, (_, _) => EditCommand("paste"), "Ctrl+V");
        var selectAllItem = Item("Select &All", Keys.None, (_, _) => EditCommand("selectAll"), "Ctrl+A");
        edit.DropDownItems.AddRange(new ToolStripItem[]
        {
            undoItem, redoItem, new ToolStripSeparator(),
            cutItem, copyItem, pasteItem, selectAllItem, new ToolStripSeparator(),
            Item("&Find...", Keys.Control | Keys.F, (_, _) => ShowFind()),
            Item("Find &Next", Keys.F3, (_, _) => FindNext(false)),
            Item("Find Pre&vious", Keys.Shift | Keys.F3, (_, _) => FindNext(true)),
        });
        edit.DropDownOpening += (_, _) =>
        {
            undoItem.Enabled = editMode && editor.CanUndo;
            redoItem.Enabled = editMode && editor.CanRedo;
            cutItem.Enabled = pasteItem.Enabled = editMode;
        };

        var view = new ToolStripMenuItem("&View");
        editModeItem = Item("&Edit Mode", Keys.Control | Keys.E, (_, _) => SetEditMode(!editMode));
        wordWrapItem = Item("&Word Wrap", Keys.None, (_, _) => SetWordWrap(!settings.WordWrap));
        themeSystemItem = Item("&System", Keys.None, (_, _) => SetTheme("System"));
        themeLightItem = Item("&Light", Keys.None, (_, _) => SetTheme("Light"));
        themeDarkItem = Item("&Dark", Keys.None, (_, _) => SetTheme("Dark"));
        var theme = new ToolStripMenuItem("&Theme");
        theme.DropDownItems.AddRange(new ToolStripItem[] { themeSystemItem, themeLightItem, themeDarkItem });
        view.DropDownItems.AddRange(new ToolStripItem[]
        {
            editModeItem, new ToolStripSeparator(),
            Item("Zoom &In", Keys.Control | Keys.Oemplus, (_, _) => Zoom(+1), "Ctrl++"),
            Item("Zoom &Out", Keys.Control | Keys.OemMinus, (_, _) => Zoom(-1), "Ctrl+-"),
            Item("&Reset Zoom", Keys.Control | Keys.D0, (_, _) => Zoom(0), "Ctrl+0"),
            new ToolStripSeparator(),
            wordWrapItem, theme,
        });

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(Item("&About MD Viewer", Keys.None, (_, _) => ShowAbout()));

        modeButton = new ToolStripMenuItem("Edit") { Alignment = ToolStripItemAlignment.Right, ToolTipText = "Toggle edit mode (Ctrl+E)" };
        modeButton.Click += (_, _) => SetEditMode(!editMode);

        menu.Items.AddRange(new ToolStripItem[] { file, edit, view, help, modeButton });
    }

    private static ToolStripMenuItem Item(string text, Keys keys, EventHandler onClick, string? keyDisplay = null)
    {
        var item = new ToolStripMenuItem(text, null, onClick);
        if (keys != Keys.None)
            item.ShortcutKeys = keys;
        if (keyDisplay != null)
            item.ShortcutKeyDisplayString = keyDisplay;
        return item;
    }

    private void BuildStatusBar()
    {
        status.SizingGrip = true;
        status.ShowItemToolTips = true;
        posLabel.Visible = false;
        status.Items.AddRange(new ToolStripItem[] { fileLabel, stateLabel, posLabel, formatLabel });
        foreach (ToolStripStatusLabel l in status.Items)
            l.Padding = new Padding(6, 0, 6, 0);
    }

    private void BuildEditor()
    {
        editor.Dock = DockStyle.Fill;
        editor.WordWrap = settings.WordWrap;
        editor.Font = CreateEditorFont(settings.EditorFontSize);
        editor.TextChanged += OnEditorTextChanged;
        editor.SelectionChanged += (_, _) => { if (!suppressEditorEvents) UpdatePosition(); };
        editor.ViewportChanged += (_, _) => { if (editMode && !suppressEditorEvents) { syncTimer.Stop(); syncTimer.Start(); } };
    }

    private static Font CreateEditorFont(float size)
    {
        foreach (var name in new[] { "Cascadia Mono", "Consolas" })
        {
            var f = new Font(name, size);
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase))
                return f;
            f.Dispose();
        }
        return new Font(FontFamily.GenericMonospace, size);
    }

    // ------------------------------------------------------------------ lifecycle

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme();
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (initialPath != null)
            OpenFile(initialPath);
        else
            UpdateTitle();

        if (initialEdit)
            SetEditMode(true);
        else
            UpdateModeUi();

        await InitializeWebViewAsync();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!ConfirmDiscardChanges())
        {
            e.Cancel = true;
            return;
        }
        SaveWindowBounds();
        settings.Save();
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            watcher?.Dispose();
            renderTimer.Dispose();
            syncTimer.Dispose();
            reloadTimer.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape && findBar.Visible)
        {
            HideFind();
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void RestoreWindowBounds()
    {
        var b = new Rectangle(settings.X, settings.Y, settings.Width, settings.Height);
        if (b.Width >= MinimumSize.Width && b.Height >= MinimumSize.Height &&
            Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(b)))
        {
            StartPosition = FormStartPosition.Manual;
            Bounds = b;
        }
        else
        {
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            Size = new Size(Math.Min(1100, area.Width - 80), Math.Min(800, area.Height - 80));
            StartPosition = FormStartPosition.CenterScreen;
        }
        if (settings.Maximized)
            WindowState = FormWindowState.Maximized;
    }

    private void SaveWindowBounds()
    {
        var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        settings.X = b.X;
        settings.Y = b.Y;
        settings.Width = b.Width;
        settings.Height = b.Height;
        settings.Maximized = WindowState == FormWindowState.Maximized;
    }

    // ------------------------------------------------------------------ WebView2

    private async Task InitializeWebViewAsync()
    {
        try
        {
            var userData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MdViewer", "WebView2");
            var options = new CoreWebView2EnvironmentOptions(
                "--disable-background-networking --disable-component-update --disable-sync --disable-default-apps " +
                "--disable-features=msSmartScreenProtection,msWebOOUI,msPdfOOUI,SpareRendererForSitePerProcess");
            var env = await CoreWebView2Environment.CreateAsync(null, userData, options);
            await web.EnsureCoreWebView2Async(env);
        }
        catch (WebView2RuntimeNotFoundException)
        {
            ShowWebViewMissing();
            return;
        }
        catch (Exception ex)
        {
            if (IsDisposed) return;
            MessageBox.Show(this, "The preview could not be started.\n\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        var core = web.CoreWebView2;
        var s = core.Settings;
        s.IsScriptEnabled = true;               // our own page script only (CSP blocks anything else)
        s.IsWebMessageEnabled = true;
        s.AreHostObjectsAllowed = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsStatusBarEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.IsZoomControlEnabled = true;
        s.AreDefaultContextMenusEnabled = true;
#if DEBUG
        s.AreDevToolsEnabled = true;
#else
        s.AreDevToolsEnabled = false;
#endif

        core.AddWebResourceRequestedFilter(AppRoot + "*", CoreWebView2WebResourceContext.All);
        core.AddWebResourceRequestedFilter(LocalUrl.Root + "*", CoreWebView2WebResourceContext.All);
        core.WebResourceRequested += OnWebResourceRequested;
        core.NavigationStarting += OnNavigationStarting;
        core.NewWindowRequested += OnNewWindowRequested;
        core.WebMessageReceived += OnWebMessageReceived;
        core.ContextMenuRequested += OnContextMenuRequested;
        core.DownloadStarting += (_, e) => e.Cancel = true;
        core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        core.ProcessFailed += (_, e) =>
        {
            if (e.ProcessFailedKind is CoreWebView2ProcessFailedKind.RenderProcessExited or CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                webReady = false;
                core.Navigate(AppRoot + "index.html");
            }
        };

        HookPreviewShortcuts();
        web.ZoomFactor = settings.PreviewZoom;
        web.ZoomFactorChanged += (_, _) => settings.PreviewZoom = web.ZoomFactor;

        core.Navigate(AppRoot + "index.html");
    }

    /// <summary>
    /// The WinForms WebView2 wrapper does not route accelerator keys to the form's menu, so
    /// shortcuts like Ctrl+O / Ctrl+E would be lost while the preview has focus. Hook the
    /// underlying controller and run keys through the normal shortcut processing.
    /// </summary>
    private void HookPreviewShortcuts()
    {
        var field = typeof(WebView2).GetField("_coreWebView2Controller", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field?.GetValue(web) is not CoreWebView2Controller controller)
            return;
        controller.AcceleratorKeyPressed += (_, e) =>
        {
            if (e.KeyEventKind is not (CoreWebView2KeyEventKind.KeyDown or CoreWebView2KeyEventKind.SystemKeyDown))
                return;
            var keyData = (Keys)e.VirtualKey | ModifierKeys;
            // Let the browser keep its own clipboard/select-all handling.
            if (keyData is (Keys.Control | Keys.C) or (Keys.Control | Keys.A) or (Keys.Control | Keys.Insert))
                return;
            var msg = Message.Create(web.Handle, 0x0100 /* WM_KEYDOWN */, (IntPtr)e.VirtualKey, IntPtr.Zero);
            if (ProcessCmdKey(ref msg, keyData))
                e.Handled = true;
        };
    }

    private void ShowWebViewMissing()
    {
        var page = new TaskDialogPage
        {
            Caption = AppName,
            Heading = "Microsoft Edge WebView2 Runtime is required",
            Text = "MD Viewer uses the WebView2 Runtime (included with Windows 11 and current Windows 10) to render Markdown. " +
                   "Install it from Microsoft and restart MD Viewer.",
            Icon = TaskDialogIcon.Warning,
        };
        var download = new TaskDialogCommandLinkButton("Open the download page");
        page.Buttons.Add(download);
        page.Buttons.Add(TaskDialogButton.Close);
        if (TaskDialog.ShowDialog(this, page) == download)
            OpenInShell("https://developer.microsoft.com/microsoft-edge/webview2/");
    }

    private void OnWebResourceRequested(object? sender, CoreWebView2WebResourceRequestedEventArgs e)
    {
        var env = web.CoreWebView2.Environment;
        if (!Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out var uri) || e.Request.Method != "GET")
        {
            e.Response = env.CreateWebResourceResponse(null, 405, "Method Not Allowed", "");
            return;
        }

        if (uri.Host == AppHost)
        {
            var name = uri.AbsolutePath.TrimStart('/');
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("web/" + name);
            if (stream == null)
            {
                e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
                return;
            }
            if (name == "index.html")
            {
                // Bake the current theme into the page so it never flashes the wrong colours.
                using var reader = new StreamReader(stream);
                var html = reader.ReadToEnd().Replace("data-theme=\"light\"", $"data-theme=\"{(palette.IsDark ? "dark" : "light")}\"");
                stream = new MemoryStream(Encoding.UTF8.GetBytes(html));
            }
            var type = Path.GetExtension(name) switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                _ => "application/octet-stream",
            };
            e.Response = env.CreateWebResourceResponse(stream, 200, "OK", $"Content-Type: {type}\r\nCache-Control: no-store");
            return;
        }

        // Local images/media referenced by the document.
        var path = LocalUrl.ToPath(uri);
        if (path == null || !MediaTypes.TryGetValue(Path.GetExtension(path), out var mime) || !File.Exists(path))
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
            return;
        }
        try
        {
            var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024);
            e.Response = env.CreateWebResourceResponse(fs, 200, "OK", $"Content-Type: {mime}\r\nCache-Control: no-cache");
        }
        catch
        {
            e.Response = env.CreateWebResourceResponse(null, 404, "Not Found", "");
        }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (e.Uri.StartsWith(AppRoot + "index.html", StringComparison.OrdinalIgnoreCase))
            return;

        e.Cancel = true;
        if (!Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri))
            return;

        if (uri.IsFile)
        {
            // A file was dropped onto the preview.
            BeginInvoke(() => OpenDropped(uri.LocalPath));
        }
        else if (e.IsUserInitiated && IsWebScheme(uri))
        {
            OpenInShell(uri.AbsoluteUri);
        }
    }

    private void OnNewWindowRequested(object? sender, CoreWebView2NewWindowRequestedEventArgs e)
    {
        e.Handled = true; // never open popups
        if (e.IsUserInitiated && Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) && IsWebScheme(uri))
            OpenInShell(uri.AbsoluteUri);
    }

    private static void OnContextMenuRequested(object? sender, CoreWebView2ContextMenuRequestedEventArgs e)
    {
        // Keep only clipboard-style entries; drop Back/Reload/Save as/Print/Inspect etc.
        var keep = new HashSet<string> { "copy", "selectAll", "copyLinkLocation", "copyImage", "copyImageLocation" };
        var items = e.MenuItems;
        for (int i = items.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(items[i].Name))
                items.RemoveAt(i);
        }
        // Tidy leading/trailing/double separators.
        for (int i = items.Count - 1; i >= 0; i--)
        {
            bool sep = items[i].Kind == CoreWebView2ContextMenuItemKind.Separator;
            if (sep && (i == 0 || i == items.Count - 1 || items[i - 1].Kind == CoreWebView2ContextMenuItemKind.Separator))
                items.RemoveAt(i);
        }
        if (items.Count == 0)
            e.Handled = true;
    }

    private void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(AppRoot, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            switch (root.GetProperty("type").GetString())
            {
                case "ready":
                    webReady = true;
                    PostTheme();
                    RenderNow();
                    break;
                case "escape":
                    if (findBar.Visible)
                        HideFind();
                    break;
                case "link":
                    var href = root.GetProperty("href").GetString();
                    if (!string.IsNullOrWhiteSpace(href))
                        BeginInvoke(() => HandleLink(href));
                    break;
            }
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            // Ignore malformed messages.
        }
    }

    private void PostMessage(Action<Utf8JsonWriter> write)
    {
        if (web.CoreWebView2 == null)
            return;
        var buffer = new ArrayBufferWriter<byte>();
        using (var w = new Utf8JsonWriter(buffer, JsonOptions))
        {
            w.WriteStartObject();
            write(w);
            w.WriteEndObject();
        }
        web.CoreWebView2.PostWebMessageAsJson(Encoding.UTF8.GetString(buffer.WrittenSpan));
    }

    private void PostTheme()
    {
        if (webReady)
            PostMessage(w => { w.WriteString("type", "theme"); w.WriteBoolean("dark", palette.IsDark); });
    }

    // ------------------------------------------------------------------ rendering

    private void ScheduleRender()
    {
        renderTimer.Stop();
        // Larger documents re-render less eagerly while typing.
        renderTimer.Interval = Math.Clamp(GetTextLengthEstimate() / 4000, 120, 1200);
        renderTimer.Start();
    }

    private int GetTextLengthEstimate() => editorHasDoc ? editor.TextLength : docText.Length;

    private async void RenderNow()
    {
        renderTimer.Stop();
        if (!webReady)
            return;

        string text = GetText();
        if (dirty && text.Length == savedText.Length && text == savedText)
        {
            dirty = false; // edits were undone back to the saved state
            UpdateTitle();
        }
        int version = ++renderVersion;
        string baseUrl = filePath != null ? LocalUrl.FromDirectory(Path.GetDirectoryName(filePath)!) : LocalUrl.Root;
        bool showWelcome = filePath == null && text.Length == 0 && !editMode && !dirty;

        string html;
        if (showWelcome)
        {
            html = "<div class=\"welcome\"><p>Open a Markdown file with <kbd>Ctrl</kbd>+<kbd>O</kbd>, or drop one here.</p>" +
                   "<p>Press <kbd>Ctrl</kbd>+<kbd>E</kbd> to start writing.</p></div>";
        }
        else
        {
            try
            {
                html = await Task.Run(() => MarkdownRenderer.Render(text));
            }
            catch (Exception ex)
            {
                html = "<pre>Rendering failed: " + WebUtility.HtmlEncode(ex.Message) + "</pre>";
            }
        }

        if (version != renderVersion || IsDisposed || !webReady)
            return;

        bool reset = resetScrollPending;
        string? anchor = anchorPending;
        resetScrollPending = false;
        anchorPending = null;
        int? line = editMode && !reset && anchor == null ? EditorTopLine() : null;

        PostMessage(w =>
        {
            w.WriteString("type", "render");
            w.WriteString("html", html);
            w.WriteString("base", baseUrl);
            w.WriteBoolean("reset", reset);
            w.WriteNumber("lines", text.AsSpan().Count('\n') + 1);
            if (anchor != null) w.WriteString("anchor", anchor);
            if (line is int l) w.WriteNumber("line", l);
        });
        if (line is int synced)
            lastSyncedLine = synced;
    }

    private void SyncPreviewToEditor()
    {
        if (!editMode || !webReady)
            return;
        int line = EditorTopLine();
        if (line == lastSyncedLine)
            return;
        lastSyncedLine = line;
        PostMessage(w => { w.WriteString("type", "line"); w.WriteNumber("line", line); });
    }

    /// <summary>0-based source line at the top of the editor viewport.</summary>
    private int EditorTopLine()
    {
        if (!editorHasDoc)
            return 0;
        var text = EditorText();
        int index = Math.Clamp(editor.FirstVisibleCharIndex, 0, text.Length);
        return text.AsSpan(0, index).Count('\n');
    }

    // ------------------------------------------------------------------ document text

    private string GetText() => editorHasDoc ? EditorText() : docText;

    private string EditorText() => editorTextCache ??= DocumentIO.NormalizeNewLines(editor.Text);

    private void LoadIntoEditor(string text)
    {
        _ = editor.Handle; // plain-text mode must be set before any text arrives
        suppressEditorEvents = true;
        try
        {
            editor.Text = text.Replace("\n", "\r\n");
            editor.ClearUndo();
            editor.Select(0, 0);
            editor.ScrollToCaret();
        }
        finally
        {
            suppressEditorEvents = false;
        }
        editorHasDoc = true;
        editorTextCache = text;
        docText = "";
        UpdatePosition();
    }

    private void OnEditorTextChanged(object? sender, EventArgs e)
    {
        if (suppressEditorEvents)
            return;
        editorTextCache = null;
        if (!dirty)
        {
            dirty = true;
            UpdateTitle();
        }
        ScheduleRender();
    }

    // ------------------------------------------------------------------ file operations

    private bool OpenFile(string path, string? anchor = null)
    {
        string full;
        string text;
        TextFormat fmt;
        try
        {
            full = Path.GetFullPath(path);
            (text, fmt) = DocumentIO.Load(full);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open \"{path}\".\n\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        filePath = full;
        format = fmt;
        dirty = false;
        savedText = text;
        changedOnDisk = false;
        lastWriteUtc = SafeLastWrite(full);
        settings.LastFolder = Path.GetDirectoryName(full);

        if (editMode || editorHasDoc)
        {
            LoadIntoEditor(text);
        }
        else
        {
            docText = text;
        }

        WatchFile();
        UpdateTitle();
        resetScrollPending = true;
        anchorPending = anchor;
        lastSyncedLine = -1;
        RenderNow();
        return true;
    }

    private void OpenWithDialog()
    {
        if (!ConfirmDiscardChanges())
            return;
        using var dlg = new OpenFileDialog
        {
            Filter = FileFilter,
            Title = "Open",
            InitialDirectory = CurrentFolder() ?? "",
        };
        if (dlg.ShowDialog(this) == DialogResult.OK)
            OpenFile(dlg.FileName);
    }

    private void OpenDropped(string path)
    {
        if (Directory.Exists(path))
            return;
        if (!ConfirmDiscardChanges())
            return;
        OpenFile(path);
    }

    private void NewDocument()
    {
        if (!ConfirmDiscardChanges())
            return;
        StopWatching();
        filePath = null;
        format = TextFormat.Default;
        dirty = false;
        savedText = "";
        changedOnDisk = false;
        if (editorHasDoc || editMode)
            LoadIntoEditor("");
        else
            docText = "";
        resetScrollPending = true;
        SetEditMode(true);
        UpdateTitle();
        RenderNow();
    }

    private bool Save() => filePath == null ? SaveAs() : WriteFile(filePath);

    private bool SaveAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = FileFilter,
            Title = "Save As",
            DefaultExt = "md",
            AddExtension = true,
            FileName = filePath != null ? Path.GetFileName(filePath) : "Untitled.md",
            InitialDirectory = CurrentFolder() ?? "",
        };
        if (dlg.ShowDialog(this) != DialogResult.OK)
            return false;

        bool folderChanged = !string.Equals(Path.GetDirectoryName(dlg.FileName), CurrentFolder(), StringComparison.OrdinalIgnoreCase);
        if (!WriteFile(dlg.FileName))
            return false;
        filePath = Path.GetFullPath(dlg.FileName);
        settings.LastFolder = Path.GetDirectoryName(filePath);
        WatchFile();
        UpdateTitle();
        if (folderChanged)
            RenderNow(); // relative images now resolve against the new folder
        return true;
    }

    private bool WriteFile(string path)
    {
        try
        {
            if (watcher != null) watcher.EnableRaisingEvents = false;
            var text = GetText();
            DocumentIO.Save(path, text, format);
            savedText = text;
            lastWriteUtc = SafeLastWrite(path);
            dirty = false;
            changedOnDisk = false;
            UpdateTitle();
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not save \"{path}\".\n\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
        finally
        {
            if (watcher != null) watcher.EnableRaisingEvents = true;
        }
    }

    /// <summary>Asks to save unsaved changes. Returns false if the user cancelled.</summary>
    private bool ConfirmDiscardChanges()
    {
        if (!dirty)
            return true;

        var save = new TaskDialogButton("&Save");
        var dontSave = new TaskDialogButton("Do&n't Save");
        var page = new TaskDialogPage
        {
            Caption = AppName,
            Heading = $"Do you want to save changes to {DisplayName}?",
            Buttons = { save, dontSave, TaskDialogButton.Cancel },
            DefaultButton = save,
        };
        var result = TaskDialog.ShowDialog(this, page);
        if (result == save)
            return Save();
        return result == dontSave;
    }

    private string DisplayName => filePath != null ? Path.GetFileName(filePath) : "Untitled";

    private string? CurrentFolder() =>
        filePath != null ? Path.GetDirectoryName(filePath) :
        Directory.Exists(settings.LastFolder) ? settings.LastFolder : null;

    private void OpenContainingFolder()
    {
        if (filePath != null && File.Exists(filePath))
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\"") { UseShellExecute = true });
    }

    private static DateTime SafeLastWrite(string path)
    {
        try { return File.GetLastWriteTimeUtc(path); } catch { return default; }
    }

    // ------------------------------------------------------------------ external change detection

    private void WatchFile()
    {
        StopWatching();
        if (filePath == null)
            return;
        try
        {
            watcher = new FileSystemWatcher(Path.GetDirectoryName(filePath)!, Path.GetFileName(filePath))
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                SynchronizingObject = this,
            };
            FileSystemEventHandler h = (_, _) => { reloadTimer.Stop(); reloadTimer.Start(); };
            watcher.Changed += h;
            watcher.Created += h;
            watcher.Renamed += (_, _) => { reloadTimer.Stop(); reloadTimer.Start(); };
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher = null; // e.g. unsupported network location
        }
    }

    private void StopWatching()
    {
        watcher?.Dispose();
        watcher = null;
    }

    private void CheckExternalChange()
    {
        if (filePath == null || !File.Exists(filePath))
            return;
        var stamp = SafeLastWrite(filePath);
        if (stamp == lastWriteUtc)
            return;

        if (dirty)
        {
            // Never clobber unsaved edits; just let the user know.
            changedOnDisk = true;
            UpdateTitle();
            return;
        }

        try
        {
            var (text, fmt) = DocumentIO.Load(filePath);
            lastWriteUtc = stamp;
            format = fmt;
            savedText = text;
            if (editorHasDoc)
            {
                int caret = editor.SelectionStart;
                LoadIntoEditor(text);
                editor.Select(Math.Min(caret, editor.TextLength), 0);
                editor.ScrollToCaret();
            }
            else
            {
                docText = text;
            }
            UpdateTitle();
            RenderNow(); // keeps the preview's scroll position
        }
        catch (IOException)
        {
            // File still being written; the watcher will fire again.
        }
    }

    // ------------------------------------------------------------------ links

    private void HandleLink(string href)
    {
        href = href.Trim();
        string pathPart;
        string? fragment = null;

        if (Uri.TryCreate(href, UriKind.Absolute, out var abs) && !abs.IsFile)
        {
            if (IsWebScheme(abs) || abs.Scheme == Uri.UriSchemeMailto)
                OpenInShell(abs.AbsoluteUri);
            return; // javascript:, data:, custom protocols, etc. are ignored
        }

        if (abs != null && abs.IsFile)
        {
            pathPart = abs.LocalPath;
            if (abs.Fragment.Length > 1)
                fragment = Uri.UnescapeDataString(abs.Fragment[1..]);
        }
        else
        {
            int hash = href.IndexOf('#');
            if (hash >= 0)
            {
                fragment = Uri.UnescapeDataString(href[(hash + 1)..]);
                href = href[..hash];
            }
            int query = href.IndexOf('?');
            if (query >= 0)
                href = href[..query];
            if (href.Length == 0)
            {
                if (fragment != null)
                    PostMessage(w => { w.WriteString("type", "anchor"); w.WriteString("id", fragment); });
                return;
            }
            if (filePath == null)
            {
                MessageBox.Show(this, "Save this document first so relative links can be resolved.", AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            string relative = Uri.UnescapeDataString(href).Replace('/', '\\').TrimStart('\\');
            pathPart = Path.Combine(Path.GetDirectoryName(filePath)!, relative);
        }

        string full;
        try
        {
            full = Path.GetFullPath(pathPart);
        }
        catch
        {
            return;
        }

        if (Directory.Exists(full))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{full}\"") { UseShellExecute = true });
            return;
        }
        if (!File.Exists(full))
        {
            MessageBox.Show(this, $"The linked file was not found:\n{full}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string ext = Path.GetExtension(full);
        if (MarkdownExtensions.Contains(ext))
        {
            if (string.Equals(full, filePath, StringComparison.OrdinalIgnoreCase))
            {
                if (fragment != null)
                    PostMessage(w => { w.WriteString("type", "anchor"); w.WriteString("id", fragment); });
                return;
            }
            if (ConfirmDiscardChanges())
                OpenFile(full, fragment);
            return;
        }
        if (BlockedExtensions.Contains(ext))
        {
            MessageBox.Show(this, $"For your safety, MD Viewer does not open program files from links:\n{full}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var confirm = MessageBox.Show(this, $"Open this file with its default application?\n\n{full}", AppName,
            MessageBoxButtons.OKCancel, MessageBoxIcon.Question);
        if (confirm == DialogResult.OK)
            OpenInShell(full);
    }

    private static bool IsWebScheme(Uri uri) => uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;

    private void OpenInShell(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not open \"{target}\".\n\n{ex.Message}", AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // ------------------------------------------------------------------ drag and drop

    private static void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true)
            e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is string[] { Length: > 0 } files)
            BeginInvoke(() => OpenDropped(files[0]));
    }

    // ------------------------------------------------------------------ modes, find, edit commands

    private void SetEditMode(bool on)
    {
        if (on && !editorHasDoc)
            LoadIntoEditor(docText);

        editMode = on;
        split.Panel1Collapsed = !on;
        if (on && split.Width > 0)
        {
            try { split.SplitterDistance = (int)(split.Width * settings.SplitRatio); } catch (InvalidOperationException) { }
        }
        UpdateModeUi();

        if (on)
        {
            editor.Focus();
            lastSyncedLine = -1;
            SyncPreviewToEditor();
        }
        else
        {
            web.Focus();
        }
        if (webReady && filePath == null && GetText().Length == 0)
            RenderNow(); // toggle the welcome message
    }

    private void UpdateModeUi()
    {
        editModeItem.Checked = editMode;
        modeButton.Text = editMode ? "View" : "Edit";
        modeButton.ToolTipText = editMode ? "Return to reading view (Ctrl+E)" : "Edit side by side (Ctrl+E)";
        posLabel.Visible = editMode;
        UpdatePosition();
    }

    private void SetWordWrap(bool on)
    {
        settings.WordWrap = on;
        editor.WordWrap = on;
        wordWrapItem.Checked = on;
    }

    private void Zoom(int direction)
    {
        if (web.CoreWebView2 == null)
            return;
        double z = direction == 0 ? 1.0 : Math.Clamp(web.ZoomFactor * (direction > 0 ? 1.1 : 1 / 1.1), 0.3, 4.0);
        web.ZoomFactor = z;
    }

    private void EditCommand(string command)
    {
        if (editMode && (editor.ContainsFocus || !web.ContainsFocus))
        {
            switch (command)
            {
                case "cut": editor.Cut(); break;
                case "copy": editor.Copy(); break;
                case "paste": editor.Paste(DataFormats.GetFormat(DataFormats.UnicodeText)); break;
                case "selectAll": editor.SelectAll(); break;
            }
        }
        else if (web.CoreWebView2 != null && command is "copy" or "selectAll")
        {
            _ = web.ExecuteScriptAsync($"document.execCommand('{command}')");
        }
    }

    private void ShowFind()
    {
        string? initial = null;
        if (editMode && editor.SelectionLength is > 0 and < 200)
            initial = editor.SelectedText;
        findBar.Visible = true;
        findBar.FocusQuery(initial);
    }

    private void HideFind()
    {
        findBar.Visible = false;
        if (editMode) editor.Focus(); else web.Focus();
    }

    private async void FindNext(bool backwards)
    {
        string q = findBar.Query;
        if (q.Length == 0)
        {
            ShowFind();
            return;
        }

        bool found;
        if (editMode)
        {
            var opts = findBar.MatchCase ? RichTextBoxFinds.MatchCase : RichTextBoxFinds.None;
            int idx;
            if (!backwards)
            {
                int start = editor.SelectionStart + editor.SelectionLength;
                idx = start < editor.TextLength ? editor.Find(q, start, opts) : -1;
                if (idx < 0) idx = editor.Find(q, 0, opts);
            }
            else
            {
                int end = editor.SelectionStart;
                idx = end > 0 ? editor.Find(q, 0, end, opts | RichTextBoxFinds.Reverse) : -1;
                if (idx < 0) idx = editor.Find(q, 0, -1, opts | RichTextBoxFinds.Reverse);
            }
            found = idx >= 0;
            if (found) editor.ScrollToCaret();
        }
        else
        {
            if (web.CoreWebView2 == null) return;
            var quoted = JsonSerializer.Serialize(q, StringJsonContext.Default.String);
            var js = $"window.find({quoted}, {(findBar.MatchCase ? "true" : "false")}, {(backwards ? "true" : "false")}, true, false, false, false)";
            found = await web.ExecuteScriptAsync(js) == "true";
        }
        findBar.SetResult(found);
    }

    // ------------------------------------------------------------------ status / title

    private void UpdateTitle()
    {
        Text = $"{(dirty ? "*" : "")}{DisplayName} - {AppName}";
        fileLabel.Text = filePath ?? "Untitled";
        fileLabel.ToolTipText = filePath;
        stateLabel.Text = changedOnDisk ? "Changed on disk" : dirty ? "Modified" : "";
        formatLabel.Text = $"{format.Label}  {format.NewLineLabel}";
    }

    private void UpdatePosition()
    {
        if (!editMode || !editorHasDoc)
            return;
        var text = EditorText();
        int caret = Math.Clamp(editor.SelectionStart, 0, text.Length);
        var before = text.AsSpan(0, caret);
        int line = before.Count('\n') + 1;
        int col = caret - (before.LastIndexOf('\n') + 1) + 1;
        posLabel.Text = $"Ln {line}, Col {col}";
    }

    private void ShowAbout()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0";
        MessageBox.Show(this,
            $"{AppName} {version}\n\nA lightweight Markdown viewer and editor for Windows.\n\n" +
            "Markdown rendering: Markdig\nSyntax highlighting: highlight.js\nPreview engine: Microsoft Edge WebView2",
            "About " + AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ theming

    private void SetTheme(string theme)
    {
        settings.Theme = theme;
        ApplyTheme();
    }

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category == UserPreferenceCategory.General && settings.Theme == "System" && IsHandleCreated)
            BeginInvoke(ApplyTheme);
    }

    private void ApplyTheme()
    {
        bool dark = settings.Theme switch
        {
            "Dark" => true,
            "Light" => false,
            _ => Theme.SystemUsesDarkTheme(),
        };
        palette = dark ? Palette.Dark : Palette.Light;
        var p = palette;

        NativeMethods.SetDarkTitleBar(Handle, dark);
        BackColor = p.Chrome;
        ForeColor = p.Text;

        var renderer = Theme.CreateRenderer(p);
        foreach (var strip in new ToolStrip[] { menu, status, findBar })
        {
            strip.Renderer = renderer;
            strip.BackColor = p.Chrome;
            strip.ForeColor = p.Text;
            Theme.ApplyToItems(strip.Items, p);
        }

        split.BackColor = dark ? p.Border : SystemColors.ControlLight;
        split.Panel1.BackColor = p.EditorBack;
        split.Panel2.BackColor = p.PageBack;
        editor.BackColor = p.EditorBack;
        editor.ForeColor = p.EditorText;
        web.DefaultBackgroundColor = p.PageBack;

        themeSystemItem.Checked = settings.Theme == "System";
        themeLightItem.Checked = settings.Theme == "Light";
        themeDarkItem.Checked = settings.Theme == "Dark";
        wordWrapItem.Checked = settings.WordWrap;

        PostTheme();
        Invalidate(true);
    }
}

[System.Text.Json.Serialization.JsonSerializable(typeof(string))]
internal sealed partial class StringJsonContext : System.Text.Json.Serialization.JsonSerializerContext
{
}
