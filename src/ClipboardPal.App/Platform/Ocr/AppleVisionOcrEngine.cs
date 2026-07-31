using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;

namespace ClipboardPal.Platform.Ocr;

/// <summary>
/// Text recognition through Apple's Vision framework, which ships with macOS 10.15+.
/// Reached via osascript's JavaScript bridge: that avoids hand-rolling Objective-C interop
/// while still using only what the OS already provides — nothing to install, nothing to download.
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class AppleVisionOcrEngine : IOcrEngine
{
    public string Name => "Apple Vision";

    public bool IsSupported => OperatingSystem.IsMacOS();

    /// <summary>
    /// Reads the image path from the environment so no quoting or escaping of user paths
    /// is needed, and joins the recognized lines top to bottom.
    /// </summary>
    private const string Script = """
        ObjC.import('Vision');
        ObjC.import('Foundation');
        function run() {
          const path = $.NSProcessInfo.processInfo.environment.objectForKey('CLIPBOARDPAL_OCR_PATH').js;
          const url = $.NSURL.fileURLWithPath(path);
          const handler = $.VNImageRequestHandler.alloc.initWithURLOptions(url, $());
          const request = $.VNRecognizeTextRequest.alloc.init;
          request.recognitionLevel = 0;
          request.usesLanguageCorrection = true;
          request.recognitionLanguages = ['ru-RU', 'en-US', 'uk-UA', 'de-DE', 'fr-FR'];
          handler.performRequestsError($([request]), $());
          const results = request.results;
          if (!results) return '';
          const lines = [];
          for (let i = 0; i < results.count; i++) {
            const candidates = results.objectAtIndex(i).topCandidates(1);
            if (candidates.count > 0) lines.push(ObjC.unwrap(candidates.objectAtIndex(0).string));
          }
          return lines.join('\n');
        }
        """;

    public Task<OcrEngineResult> RecognizeAsync(string imagePath, CancellationToken cancellationToken) =>
        Task.Run(() => Recognize(imagePath), cancellationToken);

    private OcrEngineResult Recognize(string imagePath)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/osascript",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8
            };
            psi.ArgumentList.Add("-l");
            psi.ArgumentList.Add("JavaScript");
            psi.ArgumentList.Add("-");
            psi.Environment["CLIPBOARDPAL_OCR_PATH"] = imagePath;

            using var proc = Process.Start(psi);
            if (proc is null)
                return OcrEngineResult.Unavailable("could not start osascript");

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
