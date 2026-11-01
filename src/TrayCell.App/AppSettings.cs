using System.Text.Json;

namespace TrayCell.App;

/// <summary>Small user preferences file, kept next to the app's local data.</summary>
internal sealed class AppSettings
{
    private static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrayCell");

    private static readonly string FilePath = Path.Combine(Folder, "settings.json");

    /// <summary>Device name to show on the tray icon; null means "pick automatically".</summary>
    public string? PinnedDevice { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupt settings are not worth failing over.
        }

        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Persisting preferences is best effort; the app keeps working without it.
        }
    }
}
