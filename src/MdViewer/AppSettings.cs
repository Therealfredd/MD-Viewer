using System.Text.Json;
using System.Text.Json.Serialization;

namespace MdViewer;

/// <summary>Small set of user preferences persisted to %APPDATA%\MdViewer\settings.json.</summary>
internal sealed class AppSettings
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }

    /// <summary>"System", "Light" or "Dark".</summary>
    public string Theme { get; set; } = "System";
    public bool WordWrap { get; set; } = true;
    public double PreviewZoom { get; set; } = 1.0;
    public float EditorFontSize { get; set; } = 10.5f;
    public double SplitRatio { get; set; } = 0.5;
    public string? LastFolder { get; set; }

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MdViewer");

    private static string FilePath => Path.Combine(Directory, "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                using var stream = File.OpenRead(FilePath);
                var s = JsonSerializer.Deserialize(stream, SettingsJsonContext.Default.AppSettings);
                if (s != null)
                {
                    s.PreviewZoom = Math.Clamp(s.PreviewZoom, 0.25, 5.0);
                    s.SplitRatio = Math.Clamp(s.SplitRatio, 0.1, 0.9);
                    s.EditorFontSize = Math.Clamp(s.EditorFontSize, 6f, 48f);
                    return s;
                }
            }
        }
        catch
        {
            // Corrupt or unreadable settings: fall back to defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllBytes(tmp, JsonSerializer.SerializeToUtf8Bytes(this, SettingsJsonContext.Default.AppSettings));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch
        {
            // Preferences are best-effort.
        }
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext
{
}
