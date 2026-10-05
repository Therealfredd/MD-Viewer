# MD Viewer

A small, fast Markdown viewer and editor for Windows.

MD Viewer opens `.md` files directly from File Explorer and shows them nicely formatted, the way they look on GitHub. When you need to make a change, switch to edit mode to see the raw text and a live preview side by side. It's meant to feel like Notepad for Markdown: simple, quick to open, and out of your way.

## Features

**Viewing**
- Opens Markdown files by double-clicking them in File Explorer, with *Open with*, or by dragging them onto the window
- Supports GitHub Flavored Markdown:
  - headings, bold, italics and strikethrough
  - lists, nested lists and task-list checkboxes
  - tables, quotes, footnotes and GitHub-style note/warning boxes
- Code blocks are colour-highlighted for many programming languages
- Shows images, including ones stored next to your document or in nearby folders
- Large images shrink to fit the window without being stretched
- Web links open in your default browser. Links to other Markdown files open right in MD Viewer
- Reloads the document automatically when another program changes the file

**Editing**
- Press **Ctrl+E** to edit: the raw Markdown appears on the left and a live preview on the right
- The preview updates as you type and follows your place in the document
- Open, Save, Save As, Undo, Redo and Find
- Asks whether to save before you close the window or open another file with unsaved changes
- Keeps the file's original text encoding and line endings when you save

**Look and feel**
- Light theme, dark theme, or follow your Windows setting
- Looks sharp on high-resolution displays
- Remembers its window size, position and your preferences
- Zoom the preview with Ctrl+mouse wheel or the View menu

**Lightweight and private**
- Opens in a fraction of a second and uses essentially no CPU while idle
- Works completely offline: no accounts, cloud services or telemetry
- Content inside a Markdown file is never allowed to run scripts, and links to programs are never launched

## Installation

1. Download `MdViewer-Setup-<version>.exe` from the [Releases](../../releases) page.
2. Run the installer. By default it installs for your user account only, so no administrator permission is needed.
3. Leave **"Open .md and .markdown files with MD Viewer by default"** ticked if you want Markdown files to open in MD Viewer when you double-click them.

The installer also adds MD Viewer to the *Open with* menu and to Windows' *Default apps* settings. Right-clicking a Markdown file gives you an **Edit with MD Viewer** option that opens it straight into edit mode.

> **Already use another app for `.md` files?** Windows keeps your existing choice. To switch, right-click any `.md` file, choose **Open with → Choose another app**, select **MD Viewer**, and tick **Always use this app**.

### Requirements

- Windows 10 or Windows 11 (64-bit)
- [.NET Desktop Runtime 8](https://dotnet.microsoft.com/download/dotnet/8.0) or newer. The installer will tell you if it's missing.
- Microsoft Edge WebView2 Runtime. Windows 11 and up-to-date Windows 10 already include it.

## Keyboard shortcuts

| Action | Shortcut |
|---|---|
| New document | Ctrl+N |
| Open | Ctrl+O |
| Save | Ctrl+S |
| Save As | Ctrl+Shift+S |
| Switch between viewing and editing | Ctrl+E |
| Undo / Redo | Ctrl+Z / Ctrl+Y |
| Find | Ctrl+F |
| Find next / previous | F3 / Shift+F3 |
| Zoom in / out / reset | Ctrl++ / Ctrl+- / Ctrl+0 |

## Uninstalling

Uninstall MD Viewer from **Settings → Apps** like any other program. This removes its file associations. Your preferences are kept in `%APPDATA%\MdViewer` in case you reinstall; delete that folder if you don't want them.

## Building from source

You need the .NET 8 SDK (or newer) and, to create the installer, [Inno Setup 6](https://jrsoftware.org/isinfo.php). From the repository folder, run:

```powershell
.\build.ps1
```

The installer is written to the `dist` folder.

## Acknowledgements

MD Viewer uses [Markdig](https://github.com/xoofx/markdig) to read Markdown, [highlight.js](https://highlightjs.org/) for code colouring, and Microsoft Edge WebView2 to display the formatted page.
