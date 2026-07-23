using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ClipboardPal.Core.Models;

namespace ClipboardPal.Core.Services;

/// <summary>
/// Persists history under LocalApplicationData/ClipboardPal (stable across app versions).
/// Supports both a raw JSON array (legacy) and a versioned envelope.
/// </summary>
public sealed class HistoryStore
{
    public string RootDirectory => AppDataPaths.Root;
    public string ImagesDirectory => AppDataPaths.ImagesDirectory;
    private string HistoryFile => AppDataPaths.HistoryFile;

    public HistoryStore() => AppDataPaths.EnsureCreated();

    public IReadOnlyList<ClipItem> Load()
    {
        try
        {
            if (!File.Exists(HistoryFile))
                return [];

            var json = File.ReadAllText(HistoryFile);
            var items = DeserializeHistory(json);
            items.RemoveAll(static i => i.Type == ClipItemType.Image &&
                                        (i.ImagePath is null || !File.Exists(i.ImagePath)));
            return items;
        }
        catch
        {
            AppDataPaths.BackupFile(HistoryFile, "history-corrupt");
            return [];
        }
    }

    private static List<ClipItem> DeserializeHistory(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // Versioned envelope: { "schemaVersion": N, "items": [ ... ] }
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("items", out var itemsEl))
        {
            return JsonSerializer.Deserialize<List<ClipItem>>(itemsEl.GetRawText(), AppJson.History)
                   ?? [];
        }

        // Legacy / current: bare array
        if (root.ValueKind == JsonValueKind.Array)
        {
            return JsonSerializer.Deserialize<List<ClipItem>>(json, AppJson.History) ?? [];
        }

        return [];
    }

    public async Task SaveAsync(IEnumerable<ClipItem> items, CancellationToken cancellationToken = default)
    {
        try
        {
            var list = items.ToList();
            // Keep bare-array format for compatibility with older ClipboardPal builds.
            var json = JsonSerializer.Serialize(list, AppJson.History);
            var tmp = HistoryFile + ".tmp";
            await File.WriteAllTextAsync(tmp, json, cancellationToken).ConfigureAwait(false);
            File.Move(tmp, HistoryFile, overwrite: true);
            CleanupOrphanImages(list);
        }
        catch
        {
            // Persistence failures must not crash the app.
        }
    }

    public string NewImagePath() => Path.Combine(ImagesDirectory, $"{Guid.NewGuid():N}.png");

    public static void DeleteImage(ClipItem item)
    {
        if (item.Type != ClipItemType.Image || item.ImagePath is null)
            return;
        try { File.Delete(item.ImagePath); } catch { /* ignore */ }
    }

    public static string Sha256Hex(ReadOnlySpan<byte> data) =>
        Convert.ToHexString(SHA256.HashData(data));

    public static string Sha256Text(string text) =>
        Sha256Hex(Encoding.UTF8.GetBytes(text));

    private void CleanupOrphanImages(List<ClipItem> items)
    {
        var used = items
            .Where(static i => i.ImagePath is not null)
            .Select(static i => Path.GetFileName(i.ImagePath!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.EnumerateFiles(ImagesDirectory, "*.png"))
        {
            if (!used.Contains(Path.GetFileName(file)))
            {
                try { File.Delete(file); } catch { /* ignore */ }
            }
        }
    }
}
