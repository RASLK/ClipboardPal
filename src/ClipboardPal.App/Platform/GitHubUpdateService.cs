using System.Diagnostics;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Services;

namespace ClipboardPal.Platform;

/// <summary>
/// Updates come straight from GitHub Releases: the check reads the latest-release API, the
/// install downloads the platform package and hands the swap to a detached script/installer
/// that waits for this process to exit, replaces the app and starts the new version.
/// </summary>
public sealed class GitHubUpdateService(HistoryStore store, Func<Task> quitForUpdate) : IUpdateService
{
    private const string Repo = "RASLK/ClipboardPal";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        // GitHub's API rejects requests without a User-Agent.
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ClipboardPal", CurrentVersion().ToString(3)));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    private static Version CurrentVersion()
    {
        var v = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0);
        return new Version(v.Major, v.Minor, Math.Max(v.Build, 0));
    }

    public async Task<UpdateCheck> CheckAsync(CancellationToken cancellationToken = default)
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest", cancellationToken)
            .ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        var releaseUrl = root.TryGetProperty("html_url", out var url) && url.GetString() is { } u
            ? u
            : $"https://github.com/{Repo}/releases";

        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest))
            latest = new Version(0, 0, 0);
        latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));

        var (assetName, assetUrl) = PickAsset(root);
        return new UpdateCheck(CurrentVersion(), latest, tag, releaseUrl, assetName, assetUrl);
    }

    /// <summary>Finds the release package matching this OS, CPU and packaging; null when the
    /// current run has nothing to swap (dev build, portable binary without a package).</summary>
    private static (string? Name, string? Url) PickAsset(JsonElement root)
    {
        var suffix = AssetSuffix();
        if (suffix is null || !root.TryGetProperty("assets", out var assets))
            return (null, null);

        foreach (var asset in assets.EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString() ?? string.Empty;
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return (name, asset.GetProperty("browser_download_url").GetString());
        }

        return (null, null);
    }

    private static string? AssetSuffix()
    {
        if (OperatingSystem.IsWindows())
        {
            // Only an MSI installation can be upgraded by an MSI. A portable ZIP or a dev run
            // would install a second copy elsewhere and relaunch the old binary, leaving the
            // update permanently "available".
            if (!IsWindowsMsiInstall())
                return null;
            return Environment.Is64BitOperatingSystem ? "-win64.msi" : "-win32.msi";
        }
        if (OperatingSystem.IsMacOS())
        {
            if (CurrentMacAppBundle() is null)
                return null;
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? "-macos-arm64.dmg"
                : "-macos-intel.dmg";
        }
        if (OperatingSystem.IsLinux())
            return Environment.GetEnvironmentVariable("APPIMAGE") is null ? null : "-linux-x64.AppImage";
        return null;
    }

    /// <summary>True when the running binary sits where the MSI puts it (Program Files).</summary>
    private static bool IsWindowsMsiInstall()
    {
        var current = AppContext.BaseDirectory;
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && current.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public async Task InstallAsync(UpdateCheck update, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        if (update.AssetUrl is null || update.AssetName is null)
            throw new InvalidOperationException("No release package matches this platform.");

        var dir = Path.Combine(store.RootDirectory, "updates");
        Directory.CreateDirectory(dir);
        var package = Path.Combine(dir, update.AssetName);
        await DownloadAsync(update.AssetUrl, package, progress, cancellationToken).ConfigureAwait(false);

        if (OperatingSystem.IsWindows())
            LaunchWindowsInstaller(package);
        else if (OperatingSystem.IsMacOS())
            LaunchMacInstaller(package);
        else
            LaunchLinuxInstaller(package);

        await quitForUpdate().ConfigureAwait(false);
    }

    public void OpenReleasePage(UpdateCheck update)
    {
        var url = update.ReleaseUrl;
        if (OperatingSystem.IsWindows())
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        else if (OperatingSystem.IsMacOS())
            Process.Start("open", url);
        else
            Process.Start("xdg-open", url);
    }

    private static async Task DownloadAsync(string url, string destination, IProgress<int>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? -1;

        // .part + rename: a half-written package must never be picked up as complete.
        var partial = destination + ".part";
        await using (var target = File.Create(partial))
        await using (var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        {
            var buffer = new byte[81920];
            long done = 0;
            int read;
            var lastPercent = -1;
            while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                done += read;
                if (total > 0)
                {
                    var percent = (int)(done * 100 / total);
                    if (percent != lastPercent)
                    {
                        lastPercent = percent;
                        progress?.Report(percent);
                    }
                }
            }
        }

        File.Move(partial, destination, overwrite: true);
    }

    /// <summary>MSI upgrades in place; waiting for this process to exit keeps the installer off
    /// locked files, and the relaunch works because the upgrade keeps the install location.</summary>
    private static void LaunchWindowsInstaller(string msi)
    {
        var exe = Environment.ProcessPath ?? string.Empty;
        // "&&", not "&": a failed install must not relaunch and claim success.
        var relaunch = exe.Length > 0 ? $" && start \"\" \"{exe}\"" : string.Empty;
        var wait = $"powershell -NoProfile -Command \"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue\"";
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd",
            Arguments = $"/c {wait} & msiexec /i \"{msi}\" /passive{relaunch}",
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    private void LaunchMacInstaller(string dmg)
    {
        var app = CurrentMacAppBundle()
            ?? throw new InvalidOperationException("The app is not running from a .app bundle.");

        var script = Path.Combine(store.RootDirectory, "updates", "apply-update.sh");
        // Never delete the installed app before the replacement is on disk: a full disk or a
        // corrupt download would otherwise leave the user with no app at all. Any failure
        // rolls back and relaunches the version they already had.
        File.WriteAllText(script,
            """
            #!/bin/bash
            PID="$1"; DMG="$2"; APP="$3"
            NEW="$APP.new.$$"; OLD="$APP.old.$$"; MNT=""

            fail() {
              [ -n "$MNT" ] && hdiutil detach "$MNT" -quiet 2>/dev/null
              rm -rf "$NEW"
              [ -d "$OLD" ] && [ ! -d "$APP" ] && mv "$OLD" "$APP"
              open "$APP" 2>/dev/null
              exit 1
            }

            while kill -0 "$PID" 2>/dev/null; do sleep 0.2; done

            MNT=$(mktemp -d)
            hdiutil attach -nobrowse -quiet -mountpoint "$MNT" "$DMG" || fail
            SRC=$(ls -d "$MNT"/*.app 2>/dev/null | head -1)
            [ -n "$SRC" ] || fail
            ditto "$SRC" "$NEW" || fail
            hdiutil detach "$MNT" -quiet 2>/dev/null; MNT=""
            xattr -dr com.apple.quarantine "$NEW" 2>/dev/null

            mv "$APP" "$OLD" || fail
            mv "$NEW" "$APP" || fail
            rm -rf "$OLD"
            rm -f "$DMG"
            open "$APP"
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { script, Environment.ProcessId.ToString(), dmg, app },
            UseShellExecute = false
        });
    }

    private void LaunchLinuxInstaller(string appImage)
    {
        var target = Environment.GetEnvironmentVariable("APPIMAGE")
            ?? throw new InvalidOperationException("The app is not running from an AppImage.");

        var script = Path.Combine(store.RootDirectory, "updates", "apply-update.sh");
        File.WriteAllText(script,
            """
            #!/bin/bash
            PID="$1"; NEW="$2"; TARGET="$3"
            while kill -0 "$PID" 2>/dev/null; do sleep 0.2; done
            mv -f "$NEW" "$TARGET"
            chmod +x "$TARGET"
            nohup "$TARGET" >/dev/null 2>&1 &
            """);

        Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/bash",
            ArgumentList = { script, Environment.ProcessId.ToString(), appImage, target },
            UseShellExecute = false
        });
    }

    /// <summary>Walks up from the binary to the containing .app; null under dotnet run.</summary>
    private static string? CurrentMacAppBundle()
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase) && Directory.Exists(dir))
                return dir;
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        }
        return null;
    }
}
