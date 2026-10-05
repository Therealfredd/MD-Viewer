namespace MdViewer;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();

        bool edit = false;
        string? path = null;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--edit", StringComparison.OrdinalIgnoreCase))
                edit = true;
            else if (path == null && !string.IsNullOrWhiteSpace(arg))
                path = arg;
        }

        Application.Run(new MainForm(path, edit));
    }
}
