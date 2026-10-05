using System.Text;

namespace MdViewer;

/// <summary>How a file was stored on disk, so it can be written back the same way.</summary>
internal sealed record TextFormat(Encoding Encoding, string NewLine, string Label)
{
    public static readonly TextFormat Default = new(new UTF8Encoding(false), "\r\n", "UTF-8");

    public string NewLineLabel => NewLine == "\n" ? "LF" : "CRLF";
}

internal static class DocumentIO
{
    /// <summary>Reads a text file, detecting encoding and line endings. Returned text uses '\n' only.</summary>
    public static (string Text, TextFormat Format) Load(string path)
    {
        byte[] bytes = File.ReadAllBytes(path);
        Encoding encoding;
        string label;
        int skip = 0;

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            encoding = new UTF8Encoding(true);
            label = "UTF-8 BOM";
            skip = 3;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            encoding = new UnicodeEncoding(false, true);
            label = "UTF-16 LE";
            skip = 2;
        }
        else if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            encoding = new UnicodeEncoding(true, true);
            label = "UTF-16 BE";
            skip = 2;
        }
        else if (IsValidUtf8(bytes))
        {
            encoding = new UTF8Encoding(false);
            label = "UTF-8";
        }
        else
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            encoding = Encoding.GetEncoding(0); // system ANSI code page
            label = "ANSI";
        }

        string raw = encoding.GetString(bytes, skip, bytes.Length - skip);

        string newLine = "\r\n";
        int lf = raw.IndexOf('\n');
        if (lf > 0 && raw[lf - 1] != '\r')
            newLine = "\n";

        return (NormalizeNewLines(raw), new TextFormat(encoding, newLine, label));
    }

    /// <summary>Writes text (with '\n' line endings) using the given format. Writes via a temp file so a failure never truncates the original.</summary>
    public static void Save(string path, string text, TextFormat format)
    {
        if (format.NewLine != "\n")
            text = text.Replace("\n", format.NewLine);

        byte[] preamble = format.Encoding.GetPreamble();
        byte[] body = format.Encoding.GetBytes(text);
        byte[] data = new byte[preamble.Length + body.Length];
        preamble.CopyTo(data, 0);
        body.CopyTo(data, preamble.Length);

        string dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        string tmp = Path.Combine(dir, "." + Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp");
        try
        {
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path))
                File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else
                File.Move(tmp, path);
        }
        catch (Exception) when (TryDirectWrite(path, data))
        {
            // File.Replace can fail on some network shares / locked dirs; a direct write succeeded instead.
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
        }
    }

    private static bool TryDirectWrite(string path, byte[] data)
    {
        try
        {
            File.WriteAllBytes(path, data);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string NormalizeNewLines(string s) =>
        s.Contains('\r') ? s.Replace("\r\n", "\n").Replace('\r', '\n') : s;

    private static bool IsValidUtf8(byte[] bytes)
    {
        try
        {
            new UTF8Encoding(false, true).GetCharCount(bytes);
            return true;
        }
        catch (DecoderFallbackException)
        {
            return false;
        }
    }
}
