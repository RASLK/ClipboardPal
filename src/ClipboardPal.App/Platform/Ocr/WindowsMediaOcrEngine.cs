using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace ClipboardPal.Platform.Ocr;

/// <summary>
/// Windows' own OCR (Windows.Media.Ocr), present since Windows 10 — reached through PowerShell
/// so no WinRT projection package is needed. Recognizes the languages installed in the system.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsMediaOcrEngine : IOcrEngine
{
    /// <summary>
    /// Stable marker written to stderr when no OCR-capable language pack is installed.
    /// Must stay in sync with the embedded PowerShell script.
    /// </summary>
    internal const string NoLanguagePackMarker = "CLIPBOARDPAL_OCR:NO_LANGUAGE_PACK";

    public string Name => "Windows OCR";

    public bool IsSupported => OperatingSystem.IsWindows();

    private const string Script = """
        $ErrorActionPreference = 'Stop'
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false
        $OutputEncoding = [Console]::OutputEncoding
        Add-Type -AssemblyName System.Runtime.WindowsRuntime | Out-Null
        $null = [Windows.Media.Ocr.OcrEngine,Windows.Foundation,ContentType=WindowsRuntime]
        $null = [Windows.Graphics.Imaging.BitmapDecoder,Windows.Foundation,ContentType=WindowsRuntime]
        $null = [Windows.Graphics.Imaging.SoftwareBitmap,Windows.Foundation,ContentType=WindowsRuntime]
        $null = [Windows.Storage.StorageFile,Windows.Foundation,ContentType=WindowsRuntime]
        Function Await($WinRtTask, $ResultType) {
          $asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() | Where-Object {
            $_.Name -eq 'AsTask' -and $_.ToString() -like "*$ResultType*" -and $_.GetParameters().Count -eq 1
          } | Select-Object -First 1
          if ($null -eq $asTask) { throw "AsTask not found for $ResultType" }
          $netTask = $asTask.Invoke($null, @($WinRtTask))
          $netTask.Wait(-1) | Out-Null
          if ($netTask.IsFaulted) { throw $netTask.Exception.GetBaseException() }
          $netTask.Result
        }
        $path = $env:CLIPBOARDPAL_OCR_PATH
        if (-not $path -or -not (Test-Path -LiteralPath $path)) {
          [Console]::Error.WriteLine('CLIPBOARDPAL_OCR:FILE_NOT_FOUND')
          exit 3
        }
        $file = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync($path)) ([Windows.Storage.StorageFile])
        $stream = Await ($file.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        # RecognizeAsync only accepts Gray8 / Bgra8 — PNG decoders often yield Rgba8.
        $bgra = [Windows.Graphics.Imaging.BitmapPixelFormat]::Bgra8
        $premul = [Windows.Graphics.Imaging.BitmapAlphaMode]::Premultiplied
        if ($bitmap.BitmapPixelFormat -ne $bgra -or $bitmap.BitmapAlphaMode -ne $premul) {
          $bitmap = [Windows.Graphics.Imaging.SoftwareBitmap]::Convert($bitmap, $bgra, $premul)
        }
        $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromUserProfileLanguages()
        if ($null -eq $engine) {
          foreach ($lang in [Windows.Media.Ocr.OcrEngine]::AvailableRecognizerLanguages) {
            $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage($lang)
            if ($null -ne $engine) { break }
          }
        }
        if ($null -eq $engine) {
          $engine = [Windows.Media.Ocr.OcrEngine]::TryCreateFromLanguage((New-Object Windows.Globalization.Language 'en-US'))
        }
        if ($null -eq $engine) {
          [Console]::Error.WriteLine('CLIPBOARDPAL_OCR:NO_LANGUAGE_PACK')
          exit 2
        }
        $result = Await ($engine.RecognizeAsync($bitmap)) ([Windows.Media.Ocr.OcrResult])
        [Console]::Out.Write([string]$result.Text)
        """;

    public Task<OcrEngineResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken) =>
        Task.Run(() => Recognize(imagePath), cancellationToken);

    private OcrEngineResult Recognize(string imagePath)
    {
        try
        {
            var powershell = ResolveWindowsPowerShell();
            if (powershell is null)
                return OcrEngineResult.Unavailable("Windows PowerShell not found");

            var psi = new ProcessStartInfo
            {
                FileName = powershell,
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            psi.Environment["CLIPBOARDPAL_OCR_PATH"] = imagePath;

            using var proc = Process.Start(psi);
            if (proc is null)
                return OcrEngineResult.Unavailable("could not start powershell");

            proc.StandardInput.Write(Script);
            proc.StandardInput.Close();

            // Both pipes drained before waiting so a chatty stderr cannot deadlock the child.
            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return OcrEngineResult.Failed("timed out after 30s");
            }

            var text = stdout.GetAwaiter().GetResult().Trim();
            var error = stderr.GetAwaiter().GetResult().Trim();

            // No OCR language pack: this is "engine missing", not "image has no text".
            // Returning Unavailable lets PlatformOcrService fall through to the built-in model.
            if (proc.ExitCode == 2 ||
                error.Contains(NoLanguagePackMarker, StringComparison.Ordinal))
            {
                return OcrEngineResult.Unavailable(OcrErrorKeys.NoLanguagePack);
            }

            if (proc.ExitCode == 3 ||
                error.Contains("CLIPBOARDPAL_OCR:FILE_NOT_FOUND", StringComparison.Ordinal))
            {
                return OcrEngineResult.Failed($"Image file not found: {imagePath}");
            }

            if (text.Length > 0)
                return OcrEngineResult.Success(text);

            if (proc.ExitCode != 0 || error.Length > 0)
                return OcrEngineResult.Failed(OcrText.FirstLine(error.Length > 0 ? error : $"exit code {proc.ExitCode}"));

            return OcrEngineResult.NoText();
        }
        catch (Exception ex)
        {
            return OcrEngineResult.Failed(OcrText.Describe(ex));
        }
    }

    /// <summary>
    /// Prefer the Windows PowerShell 5.1 host under System32 — it has the WinRT bridge
    /// (System.Runtime.WindowsRuntime) that pwsh / PowerShell 7 does not.
    /// </summary>
    private static string? ResolveWindowsPowerShell()
    {
        var system = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var candidate = Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe");
        if (File.Exists(candidate))
            return candidate;

        // Fall back to PATH only if the usual install is missing.
        return "powershell.exe";
    }
}
