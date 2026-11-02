namespace TrayCell.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // A tray app should never die on a message-loop exception. WinForms throws one, for
        // example, when the active keyboard layout changes to a culture the runtime cannot
        // resolve; catching it keeps the icon alive instead of showing a crash dialog.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, args) => LogException(args.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, args) => LogException(args.ExceptionObject as Exception);

        using var context = new TrayApplicationContext();
        Application.Run(context);
    }

    /// <summary>Appends the failure to %LOCALAPPDATA%\TrayCell\error.log; never throws.</summary>
    private static void LogException(Exception? exception)
    {
        if (exception is null)
        {
            return;
        }

        try
        {
            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayCell");
            Directory.CreateDirectory(folder);
            File.AppendAllText(
                Path.Combine(folder, "error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception logFailure) when (logFailure is IOException or UnauthorizedAccessException)
        {
            // Nothing sensible left to do if even the log write fails.
        }
    }
}
