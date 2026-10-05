---
title: Front matter is hidden
---

# MD Viewer feature tour

This document exercises the Markdown features MD Viewer renders. Open it, then press **Ctrl+E** to edit it side by side.

## Inline formatting

Plain paragraph text with **bold**, *italic*, ***bold italic***, ~~strikethrough~~, `inline code`, and <kbd>Ctrl</kbd>+<kbd>S</kbd>.
A soft line break stays in the same paragraph,  
but two trailing spaces force a hard break.

Escaped characters: \*not italic\*, \# not a heading, \`not code\`, 5 \> 3, and a literal backslash \\.

Autolinks: https://commonmark.org and <https://github.github.com/gfm/>.

## Headings

### Third level
#### Fourth level
##### Fifth level
###### Sixth level

## Blockquotes

> Markdown is intended to be as easy-to-read and easy-to-write as is feasible.
>
> > Nested quotes work too.

> [!NOTE]
> GitHub-style alerts are supported.

> [!WARNING]
> Be careful with this one.

## Lists

1. First ordered item
2. Second ordered item
   - Nested unordered item
   - Another nested item
     1. Deeply nested ordered
     2. Still going
3. Third ordered item

- Unordered item
- Another one
  * Nested with a different marker

### Task list

- [x] Render Markdown correctly
- [x] Register `.md` with Windows
- [ ] Take over the world
  - [x] Nested task

---

## Code

Inline `Console.WriteLine("hi")` and fenced blocks with highlighting:

```csharp
// C#
public static int Fib(int n) => n < 2 ? n : Fib(n - 1) + Fib(n - 2);
```

```python
def greet(name: str) -> str:
    """Return a greeting."""
    return f"Hello, {name}!"
```

```js
const total = [1, 2, 3].reduce((a, b) => a + b, 0);
console.log(`total = ${total}`);
```

```json
{ "name": "md-viewer", "version": 1, "private": true }
```

```diff
- removed line
+ added line
```

```
A fence with no language is shown without highlighting.
```

    Indented code blocks work as well.

## Tables

| Feature        | Supported | Notes                      |
|:---------------|:---------:|---------------------------:|
| Tables         | Yes       | With alignment             |
| Task lists     | Yes       | Read-only checkboxes       |
| Syntax colours | Yes       | Via highlight.js, offline  |

## Links

- External link (opens in your browser): [CommonMark](https://commonmark.org)
- Local Markdown link: [another document](sub/other.md)
- Local link to a heading in another document: [other doc, section two](sub/other.md#section-two)
- Link to a heading in this document: [jump to Images](#images)
- Footnote reference[^1]

## Images

Relative image (same folder tree):

![Diagram](images/diagram.svg)

Raster image with a title:

![Gradient](images/gradient.png "A generated PNG")

A large image scales down to fit the window while keeping its aspect ratio:

![Wide](images/wide.png)

## Embedded HTML

<details>
<summary>Click to expand</summary>

Hidden content, *with Markdown* inside.

</details>

<p align="center"><b>Centered HTML paragraph</b> with <mark>highlighted</mark> text and H<sub>2</sub>O / E = mc<sup>2</sup>.</p>

<div style="border:1px dashed #888; padding:8px">A div with inline style.</div>

## Safety

The following script, event handler and javascript: link are stripped and never run:

<script>document.body.innerHTML = "<h1>SCRIPT RAN</h1>";</script>
<img src="does-not-exist.png" onerror="document.body.innerHTML='<h1>ONERROR RAN</h1>'" alt="broken image (handler removed)">
<a href="javascript:alert('xss')">javascript: link (inert)</a>
<iframe src="https://example.com"></iframe>

[^1]: Footnotes are collected at the bottom of the document.
