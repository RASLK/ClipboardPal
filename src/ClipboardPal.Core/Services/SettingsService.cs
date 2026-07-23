using System.Text.Json;
using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Services;

public sealed class SettingsService : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private int _saveRequested;

    public AppSettings Settings { get; }

    public SettingsService()
    {
        AppDataPaths.EnsureCreated();
        Settings = LoadAndMigrate();
        Settings.PropertyChanged += (_, _) => ScheduleSave();
        _ = DebounceLoopAsync(_cts.Token);
    }

    private static AppSettings LoadAndMigrate()
    {
        var path = AppDataPaths.SettingsFile;
        if (!File.Exists(path))
        {
            var fresh = new AppSettings { SchemaVersion = AppDataPaths.CurrentSchemaVersion };
            return fresh;
        }

        try
        {
            var json = File.ReadAllText(path);
            var settings = JsonSerializer.Deserialize<AppSettings>(json, AppJson.Settings)
                           ?? new AppSettings();

            Migrate(settings, json);
            settings.SchemaVersion = AppDataPaths.CurrentSchemaVersion;
            return settings;
        }
        catch
        {
            AppDataPaths.BackupFile(path, "settings-corrupt");
            return new AppSettings { SchemaVersion = AppDataPaths.CurrentSchemaVersion };
        }
    }

    /// <summary>
    /// Bring older settings.json forward without dropping known values.
    /// Unknown future fields are ignored by the serializer; missing fields keep defaults.
    /// </summary>
    private static void Migrate(AppSettings settings, string rawJson)
    {
        var from = settings.SchemaVersion;
        if (from <= 0)
            from = 1;

        // v1 → v2: introduce HidePopupOnOutsideClick (default true) if absent in JSON.
        if (from < 2)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (!doc.RootElement.TryGetProperty("hidePopupOnOutsideClick", out _) &&
                    !doc.RootElement.TryGetProperty("HidePopupOnOutsideClick", out _))
                {
                    settings.HidePopupOnOutsideClick = true;
                }
            }
            catch
            {
                settings.HidePopupOnOutsideClick = true;
            }
        }

        // v2 → v3: language catalog (drop System; English is default).
        if (from < 3)
        {
            try
            {
                using var doc = JsonDocument.Parse(rawJson);
                if (doc.RootElement.TryGetProperty("Language", out var lang) ||
                    doc.RootElement.TryGetProperty("language", out lang))
                {
                    var name = lang.ValueKind == JsonValueKind.String ? lang.GetString() : null;
                    if (string.Equals(name, "System", StringComparison.OrdinalIgnoreCase))
                        settings.Language = UiLanguage.English;
                }
            }
            catch
            {
                // keep deserialized value
            }
        }

        // Future migrations: if (from < 4) { ... }
    }

    private void ScheduleSave() => Interlocked.Exchange(ref _saveRequested, 1);

    private async Task DebounceLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
        while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
        {
            if (Interlocked.Exchange(ref _saveRequested, 0) == 1)
                await SaveAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Settings.SchemaVersion = AppDataPaths.CurrentSchemaVersion;
            var json = JsonSerializer.Serialize(Settings, AppJson.Settings);
            var tmp = AppDataPaths.SettingsFile + ".tmp";
            await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
            File.Move(tmp, AppDataPaths.SettingsFile, overwrite: true);
        }
        catch
        {
            // ignore
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
        _cts.Dispose();
        await SaveAsync().ConfigureAwait(false);
    }
}
