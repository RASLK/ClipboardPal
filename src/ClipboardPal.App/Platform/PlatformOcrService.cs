using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using ClipboardPal.Core.Abstractions;
using ClipboardPal.Core.Services;
using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;

namespace ClipboardPal.Platform;

/// <summary>
/// Cross-platform OCR: Tesseract (in-process + CLI) with auto-downloaded tessdata,
/// WinRT boost on Windows, and conda-forge binary bootstrap on macOS/Linux.
/// </summary>
public sealed class PlatformOcrService : IOcrService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly string[] TessLanguages = ["eng", "rus"];
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly string _tessDataDir;
    private readonly string _binDir;

    private bool _ready;
    private string? _cliPath;

    public PlatformOcrService(HistoryStore store)
    {
        var root = Path.Combine(store.RootDirectory, "ocr");
        _tessDataDir = Path.Combine(root, "tessdata");
        _binDir = Path.Combine(root, "bin");
        Directory.CreateDirectory(_tessDataDir);
        Directory.CreateDirectory(_binDir);
    }

    public async Task<string?> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
            return null;

        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureReadyAsync(cancellationToken).ConfigureAwait(false);
            return await Task.Run(() => RecognizeCore(imagePath), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            Gate.Release();
        }
    }

    private string? RecognizeCore(string imagePath)
    {
        if (OperatingSystem.IsWindows())
        {
            var winRt = TryRecognizeViaPowerShell(imagePath);
            if (!string.IsNullOrWhiteSpace(winRt))
                return winRt;
        }

        var engineText = TryRecognizeViaEngine(imagePath);
        if (!string.IsNullOrWhiteSpace(engineText))
            return engineText;

        return TryRecognizeViaCli(imagePath);
    }

    private async Task EnsureReadyAsync(CancellationToken cancellationToken)
    {
        if (_ready)
            return;

        await EnsureTessDataAsync(cancellationToken).ConfigureAwait(false);
        _cliPath = await ResolveTesseractCliAsync(cancellationToken).ConfigureAwait(false);
        _ready = true;
    }

    private async Task EnsureTessDataAsync(CancellationToken cancellationToken)
    {
        foreach (var lang in TessLanguages)
        {
            var target = Path.Combine(_tessDataDir, $"{lang}.traineddata");
            if (File.Exists(target) && new FileInfo(target).Length > 10_000)
                continue;

            var url = $"https://github.com/tesseract-ocr/tessdata_fast/raw/main/{lang}.traineddata";
            await using var remote = await Http.GetStreamAsync(url, cancellationToken)
                .ConfigureAwait(false);
            var tmp = target + ".tmp";
            await using (var file = File.Create(tmp))
                await remote.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            File.Move(tmp, target, overwrite: true);
        }
    }

    private string? TryRecognizeViaEngine(string imagePath)
    {
        try
        {
            using var engine = new Engine(_tessDataDir, "eng+rus", EngineMode.LstmOnly);
            using var img = Image.LoadFromFile(imagePath);
            using var page = engine.Process(img);
            var text = page.Text?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private string? TryRecognizeViaCli(string imagePath)
    {
        var exe = _cliPath;
        if (string.IsNullOrWhiteSpace(exe))
            return null;

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };

            // Bundled / conda binaries need their lib dir on Unix.
            var libDir = Path.Combine(_binDir, "lib");
            if (Directory.Exists(libDir))
            {
                var key = OperatingSystem.IsMacOS() ? "DYLD_LIBRARY_PATH" : "LD_LIBRARY_PATH";
                var existing = Environment.GetEnvironmentVariable(key);
                psi.Environment[key] = string.IsNullOrEmpty(existing)
                    ? libDir
                    : libDir + Path.PathSeparator + existing;
            }

            psi.ArgumentList.Add(imagePath);
            psi.ArgumentList.Add("stdout");
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add("eng+rus");
            psi.ArgumentList.Add("--tessdata-dir");
            psi.ArgumentList.Add(_tessDataDir);
            psi.ArgumentList.Add("--oem");
            psi.ArgumentList.Add("1");

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(60_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            var text = output?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }

    private async Task<string?> ResolveTesseractCliAsync(CancellationToken cancellationToken)
    {
        foreach (var candidate in EnumerateLocalCliCandidates())
        {
            if (await IsWorkingCliAsync(candidate, cancellationToken).ConfigureAwait(false))
                return candidate;
        }

        if (OperatingSystem.IsWindows())
        {
            var bundled = TryLocateBundledWindowsTesseract();
            if (bundled is not null &&
                await IsWorkingCliAsync(bundled, cancellationToken).ConfigureAwait(false))
                return bundled;
        }
        else
        {
            var bootstrapped = await TryBootstrapUnixTesseractAsync(cancellationToken)
                .ConfigureAwait(false);
            if (bootstrapped is not null &&
                await IsWorkingCliAsync(bootstrapped, cancellationToken).ConfigureAwait(false))
                return bootstrapped;
        }

        return null;
    }

    private IEnumerable<string> EnumerateLocalCliCandidates()
    {
        var local = Path.Combine(_binDir, "bin", "tesseract");
        if (File.Exists(local))
            yield return local;

        var localFlat = Path.Combine(_binDir, "tesseract");
        if (File.Exists(localFlat))
            yield return localFlat;

        yield return "tesseract";
        if (OperatingSystem.IsWindows())
        {
            yield return "tesseract.exe";
            var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            yield return Path.Combine(pf, "Tesseract-OCR", "tesseract.exe");
        }
        else if (OperatingSystem.IsMacOS())
        {
            yield return "/opt/homebrew/bin/tesseract";
            yield return "/usr/local/bin/tesseract";
            yield return "/opt/local/bin/tesseract";
        }
        else
        {
            yield return "/usr/bin/tesseract";
            yield return "/usr/local/bin/tesseract";
        }
    }

    private string? TryLocateBundledWindowsTesseract()
    {
        try
        {
            var arch = Environment.Is64BitProcess ? "x64" : "x86";
            var baseDir = AppContext.BaseDirectory;
            foreach (var candidate in new[]
                     {
                         Path.Combine(baseDir, "tesseract.exe"),
                         Path.Combine(baseDir, arch, "tesseract.exe"),
                         Path.Combine(_binDir, "tesseract.exe")
                     })
            {
                if (File.Exists(candidate))
                    return candidate;
            }

            var nugetRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget", "packages", "tesseractocr");
            if (!Directory.Exists(nugetRoot))
                return null;

            foreach (var verDir in Directory.GetDirectories(nugetRoot).OrderDescending())
            {
                var srcDir = Path.Combine(verDir, arch);
                var exe = Path.Combine(srcDir, "tesseract.exe");
                if (!File.Exists(exe))
                    continue;

                foreach (var file in Directory.GetFiles(srcDir))
                {
                    var dest = Path.Combine(_binDir, Path.GetFileName(file));
                    if (!File.Exists(dest))
                        File.Copy(file, dest, overwrite: false);
                }

                var destExe = Path.Combine(_binDir, "tesseract.exe");
                return File.Exists(destExe) ? destExe : exe;
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }

    private async Task<string?> TryBootstrapUnixTesseractAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subdir = GetCondaSubdir();
            if (subdir is null)
                return null;

            await EnsureCondaPackageAsync("leptonica", subdir, cancellationToken).ConfigureAwait(false);
            await EnsureCondaPackageAsync("libarchive", subdir, cancellationToken).ConfigureAwait(false);
            await EnsureCondaPackageAsync("tesseract", subdir, cancellationToken).ConfigureAwait(false);

            var exe = Path.Combine(_binDir, "bin", "tesseract");
            if (!File.Exists(exe))
                return null;

            TryChmodX(exe);
            return exe;
        }
        catch
        {
            return null;
        }
    }

    private async Task EnsureCondaPackageAsync(
        string packageName,
        string subdir,
        CancellationToken cancellationToken)
    {
        var stamp = Path.Combine(_binDir, $".{packageName}.{subdir}.ok");
        if (File.Exists(stamp))
            return;

        var api = $"https://api.anaconda.org/package/conda-forge/{packageName}/files";
        await using var stream = await Http.GetStreamAsync(api, cancellationToken).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        string? downloadUrl = null;
        foreach (var file in doc.RootElement.EnumerateArray())
        {
            if (!file.TryGetProperty("attrs", out var attrs))
                continue;
            if (!attrs.TryGetProperty("subdir", out var sd) ||
                !string.Equals(sd.GetString(), subdir, StringComparison.Ordinal))
                continue;
            if (!file.TryGetProperty("download_url", out var urlEl))
                continue;

            var url = urlEl.GetString();
            if (string.IsNullOrWhiteSpace(url))
                continue;

            var basename = file.TryGetProperty("basename", out var bn) ? bn.GetString() ?? "" : url;
            if (basename.Contains("cuda", StringComparison.OrdinalIgnoreCase))
                continue;

            downloadUrl = url.StartsWith("http", StringComparison.Ordinal) ? url : "https:" + url;
            break;
        }

        if (downloadUrl is null)
            return;

        var archivePath = Path.Combine(_binDir, $"{packageName}-pkg.tar.bz2");
        await using (var remote = await Http.GetStreamAsync(downloadUrl, cancellationToken)
                         .ConfigureAwait(false))
        await using (var file = File.Create(archivePath))
            await remote.CopyToAsync(file, cancellationToken).ConfigureAwait(false);

        ExtractWithSystemTar(archivePath, _binDir);
        try { File.Delete(archivePath); } catch { /* ignore */ }
        await File.WriteAllTextAsync(stamp, DateTime.UtcNow.ToString("O"), cancellationToken)
            .ConfigureAwait(false);
    }

    private static void ExtractWithSystemTar(string archivePath, string destination)
    {
        using var proc = Process.Start(new ProcessStartInfo
        {
            FileName = "tar",
            ArgumentList = { "-xjf", archivePath, "-C", destination },
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        });
        if (proc is null)
            throw new InvalidOperationException("tar not available");
        proc.WaitForExit(120_000);
        if (proc.ExitCode != 0)
            throw new InvalidOperationException(proc.StandardError.ReadToEnd());
    }

    private static string? GetCondaSubdir()
    {
        if (OperatingSystem.IsMacOS())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "osx-arm64" : "osx-64";
        if (OperatingSystem.IsLinux())
            return RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "linux-aarch64" : "linux-64";
        return null;
    }

    private static async Task<bool> IsWorkingCliAsync(string exe, CancellationToken cancellationToken)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.ArgumentList.Add("--version");
            using var proc = Process.Start(psi);
            if (proc is null) return false;
            _ = await proc.StandardOutput.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            await proc.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return proc.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void TryChmodX(string path)
    {
        try
        {
            using var proc = Process.Start(new ProcessStartInfo
            {
                FileName = "chmod",
                ArgumentList = { "+x", path },
                UseShellExecute = false,
                CreateNoWindow = true
            });
            proc?.WaitForExit(5000);
        }
        catch
        {
            // ignore
        }
    }

    private static string? TryRecognizeViaPowerShell(string imagePath)
    {
        try
        {
            var ps = """
                Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
                $null = [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
                $null = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
                $null = [Windows.Storage.StorageFile,Windows.Foundation,ContentType=WindowsRuntime]
                Function Await($WinRtTask, $ResultType) {
                  $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
                    $_.Name -eq 'AsTask' -and $_.ToString() -like "*$ResultType*" -and $_.GetParameters().Count -eq 1
                  } | Select-Object -First 1
                  $netTask = $asTask.Invoke($null, @($WinRtTask))
                  $netTask.Wait(-1) | Out-Null
                  $netTask.Result
                }
                $path = $env:CLIPBOARDPAL_OCR_PATH
                $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
                $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
                $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
                $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
                $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
                if ($null -eq $engine) {
                  $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage((New-Object Windows.Globalization.Language 'en-US'))
                }
                if ($null -eq $engine) { return }
                $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
                $result.Text
                """;

            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            psi.Environment["CLIPBOARDPAL_OCR_PATH"] = imagePath;

            using var proc = Process.Start(psi);
            if (proc is null) return null;
            proc.StandardInput.Write(ps);
            proc.StandardInput.Close();
            var output = proc.StandardOutput.ReadToEnd();
            if (!proc.WaitForExit(20000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return null;
            }

            var text = output?.Trim();
            return string.IsNullOrWhiteSpace(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
