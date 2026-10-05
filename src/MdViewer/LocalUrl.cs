namespace MdViewer;

/// <summary>
/// Maps local file-system paths to URLs on a private virtual host that the preview can load
/// (e.g. C:\docs\img.png -> https://local.mdviewer/C%3A/docs/img.png) and back again.
/// Using a real hierarchical URL means relative references like "../img/a.png" resolve naturally.
/// </summary>
internal static class LocalUrl
{
    public const string Host = "local.mdviewer";
    public const string Root = "https://" + Host + "/";

    public static string FromDirectory(string directory)
    {
        var url = FromPath(directory);
        return url.EndsWith('/') ? url : url + "/";
    }

    public static string FromPath(string path)
    {
        path = Path.GetFullPath(path);
        string prefix = "";
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            prefix = "UNC/";
            path = path[2..];
        }
        var segments = path.Replace('\\', '/').Split('/');
        return Root + prefix + string.Join("/", segments.Select(Uri.EscapeDataString));
    }

    public static string? ToPath(Uri uri)
    {
        var segments = uri.AbsolutePath.TrimStart('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
        if (segments.Length == 0)
            return null;

        string path = segments[0] == "UNC"
            ? @"\\" + string.Join('\\', segments.Skip(1))
            : string.Join('\\', segments);

        if (!Path.IsPathFullyQualified(path) || path.Contains('\0'))
            return null;
        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return null;
        }
    }
}
