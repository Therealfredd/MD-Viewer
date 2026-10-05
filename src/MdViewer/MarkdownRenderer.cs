using System.Globalization;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Renderers;
using Markdig.Renderers.Html;

namespace MdViewer;

/// <summary>Converts Markdown (GitHub Flavored) to HTML. Thread-safe.</summary>
internal static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseYamlFrontMatter()
        .UseAlertBlocks()
        .UsePipeTables()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .UseTaskLists()
        .UseAutoLinks()
        .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
        .UseFootnotes()
        .Build();

    public static string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);

        // Tag top-level blocks with their source line so the preview can follow the editor's scroll position.
        foreach (var block in document)
            block.GetAttributes().AddProperty("data-line", block.Line.ToString(CultureInfo.InvariantCulture));

        using var writer = new StringWriter(CultureInfo.InvariantCulture);
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.Render(document);
        writer.Flush();
        return writer.ToString();
    }
}
