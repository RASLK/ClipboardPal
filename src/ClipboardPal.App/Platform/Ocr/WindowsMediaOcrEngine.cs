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
    public string Name => "Windows OCR";

    public bool IsSupported => OperatingSystem.IsWindows();

    private const string Script = """
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

    public Task<OcrEngineResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken) =>
        Task.Run(() => Recognize(imagePath), cancellationToken);

    private OcrEngineResult Recognize(string imagePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command -",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.Environment["CLIPBOARDPAL_OCR_PATH"] = imagePath;

            using var proc = Process.Start(psi);
            if (proc is null)
                return OcrEngineResult.Unavailable("could not start powershell");

            proc.StandardInput.Write(Script);
            proc.StandardInput.Close();

            var stdout = proc.StandardOutput.ReadToEndAsync();
            var stderr = proc.StandardError.ReadToEndAsync();
            if (!proc.WaitForExit(30_000))
            {
                try { proc.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return OcrEngineResult.Failed("timed out after 30s");
            }

            var text = stdout.GetAwaiter().GetResult().Trim();
            if (text.Length > 0)
                return OcrEngineResult.Success(text);

            var error = stderr.GetAwaiter().GetResult().Trim();
            return error.Length == 0
                ? OcrEngineResult.NoText()
                : OcrEngineResult.Failed(OcrText.FirstLine(error));
        }
        catch (Exception ex)
        {
            return OcrEngineResult.Failed(OcrText.Describe(ex));
        }
    }
}
