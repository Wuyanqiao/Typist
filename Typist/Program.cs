namespace Typist;

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Load settings from disk
        var settings = AppSettings.Load();

        // Run the application
        Application.Run(new Form1(settings));

        // Save settings on exit
        settings.Save();
    }
}
