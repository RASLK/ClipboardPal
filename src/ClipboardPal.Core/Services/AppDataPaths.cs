using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClipboardPal.Core.Services;

/// <summary>Stable LocalAppData root shared across app versions.</summary>
public static class AppDataPaths
{
    public const int CurrentSchemaVersion = 3;

    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ClipboardPal");

    public static string SettingsFile => Path.Combine(Root, "settings.json");
    public static string HistoryFile => Path.Combine(Root, "history.json");
    public static string ImagesDirectory => Path.Combine(Root, "images");
    public static string BackupsDirectory => Path.Combine(Root, "backups");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ImagesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }

    /// <summary>Copy file to backups/ before risky rewrite. Keeps last few copies.</summary>
    public static void BackupFile(string sourcePath, string label)
    {
        try
        {
            if (!File.Exists(sourcePath))
                return;

            EnsureCreated();
            var name = $"{label}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
            File.Copy(sourcePath, Path.Combine(BackupsDirectory, name), overwrite: false);

            var old = Directory.GetFiles(BackupsDirectory, $"{label}-*.json")
                .OrderDescending()
                .Skip(8);
            foreach (var f in old)
            {
                try { File.Delete(f); } catch { /* ignore */ }
            }
        }
        catch
        {
            // Backups must never block the app.
        }
    }
}

/// <summary>Forward-compatible JSON options: unknown fields ignored, enums as strings.</summary>
public static class AppJson
{
    public static JsonSerializerOptions Settings { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) },
        // Missing members keep CLR defaults; unknown JSON members are ignored.
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    public static JsonSerializerOptions History { get; } = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };
}
