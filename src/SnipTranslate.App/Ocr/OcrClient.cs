using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Windows.Media;

namespace SnipTranslate.Ocr;

internal sealed class OcrClient : IDisposable
{
    private const string PipeName = "SnipTranslate.Ocr.v1";
    private readonly object _sync = new();
    private Process? _worker;

    internal void Prepare()
    {
        lock (_sync)
        {
            if (_worker is { HasExited: false })
            {
                return;
            }

            var executable = Path.Combine(AppContext.BaseDirectory, "ocr", "SnipTranslate.OcrWorker.exe");
            if (!File.Exists(executable))
            {
                throw new FileNotFoundException("找不到 OCR Worker。", executable);
            }

            _worker?.Dispose();
            _worker = Process.Start(new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(executable)!
            }) ?? throw new InvalidOperationException("无法启动 OCR Worker。");
        }
    }

    internal async Task<OcrResult> RecognizeAsync(
        BitmapSource bitmap,
        string language,
        bool enableAngleDetection,
        string quality,
        bool forceEnhanced,
        CancellationToken cancellationToken)
    {
        Prepare();
        var normalizedLanguage = language.ToLowerInvariant();
        var primaryBitmap = normalizedLanguage == "en" || forceEnhanced
            ? EnhanceEnglish(bitmap)
            : bitmap;
        var primary = await RecognizeOnceAsync(
            primaryBitmap,
            normalizedLanguage,
            enableAngleDetection,
            quality,
            normalizedLanguage == "en" || forceEnhanced,
            cancellationToken);

        if (normalizedLanguage != "auto")
        {
            return new OcrResult(primary.Text, primary.ElapsedMilliseconds, primary.Confidence, false, false,
                normalizedLanguage == "en" || forceEnhanced);
        }

        var english = await RecognizeOnceAsync(
            EnhanceEnglish(bitmap),
            "en",
            enableAngleDetection,
            "fast",
            true,
            cancellationToken);
        var useEnglish = PreferEnglishResult(primary, english);
        var selected = useEnglish ? english : primary;
        return new OcrResult(selected.Text, primary.ElapsedMilliseconds + english.ElapsedMilliseconds,
            selected.Confidence, true, useEnglish, useEnglish || forceEnhanced);
    }

    private async Task<OcrWireResponse> RecognizeOnceAsync(
        BitmapSource bitmap,
        string language,
        bool enableAngleDetection,
        string quality,
        bool enhanced,
        CancellationToken cancellationToken)
    {
        var requestId = Guid.NewGuid().ToString("N");
        var imagePath = Path.Combine(Path.GetTempPath(), $"sniptranslate-{requestId}.png");

        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            await using (var file = File.Create(imagePath))
            {
                encoder.Save(file);
            }

            await using var pipe = new NamedPipeClientStream(
                ".",
                PipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            await pipe.ConnectAsync(TimeSpan.FromSeconds(20), cancellationToken);

            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            var request = JsonSerializer.Serialize(new
            {
                RequestId = requestId,
                ImagePath = imagePath,
                Language = language,
                EnableAngleDetection = enableAngleDetection,
                Quality = quality,
                Enhanced = enhanced
            });
            await writer.WriteLineAsync(request.AsMemory(), cancellationToken);
            var responseJson = await reader.ReadLineAsync(cancellationToken)
                ?? throw new EndOfStreamException("OCR Worker 没有返回结果。");
            var response = JsonSerializer.Deserialize<OcrWireResponse>(responseJson)
                ?? throw new InvalidDataException("OCR Worker 返回格式无效。");

            if (!response.Success)
            {
                throw new InvalidOperationException(response.Error ?? "OCR 识别失败。");
            }

            return response;
        }
        finally
        {
            try
            {
                File.Delete(imagePath);
            }
            catch (IOException)
            {
                // The operating system will clean the temp directory later.
            }
        }
    }

    private static BitmapSource EnhanceEnglish(BitmapSource source)
    {
        var shortestSide = Math.Max(1, Math.Min(source.PixelWidth, source.PixelHeight));
        var scale = shortestSide < 240 ? Math.Min(3.0, 540.0 / shortestSide)
            : shortestSide < 520 ? 1.75
            : 1.25;
        var scaled = new TransformedBitmap(source, new ScaleTransform(scale, scale));
        var converted = new FormatConvertedBitmap(scaled, PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var pixels = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(pixels, stride, 0);

        const double contrast = 1.32;
        for (var index = 0; index < pixels.Length; index += 4)
        {
            var gray = 0.114 * pixels[index] + 0.587 * pixels[index + 1] + 0.299 * pixels[index + 2];
            var adjusted = (byte)Math.Clamp((gray - 127.5) * contrast + 127.5, 0, 255);
            pixels[index] = adjusted;
            pixels[index + 1] = adjusted;
            pixels[index + 2] = adjusted;
        }

        var enhanced = BitmapSource.Create(converted.PixelWidth, converted.PixelHeight,
            Math.Max(96, source.DpiX * scale), Math.Max(96, source.DpiY * scale),
            PixelFormats.Bgra32, null, pixels, stride);
        enhanced.Freeze();
        return enhanced;
    }

    private static bool PreferEnglishResult(OcrWireResponse primary, OcrWireResponse english)
    {
        if (string.IsNullOrWhiteSpace(english.Text)) return false;
        if (string.IsNullOrWhiteSpace(primary.Text)) return true;

        var primaryCjk = primary.Text.Count(IsCjk);
        var primaryLatin = primary.Text.Count(IsLatin);
        if (primaryCjk >= 2 && primaryCjk > primaryLatin * 0.35) return false;

        return CandidateScore(english, englishCandidate: true) > CandidateScore(primary, englishCandidate: false) + 0.025;
    }

    private static double CandidateScore(OcrWireResponse result, bool englishCandidate)
    {
        var visible = result.Text.Where(character => !char.IsWhiteSpace(character)).ToArray();
        if (visible.Length == 0) return 0;
        var readable = visible.Count(character => char.IsLetterOrDigit(character) || char.IsPunctuation(character));
        var latin = visible.Count(IsLatin);
        var readableRatio = (double)readable / visible.Length;
        var latinRatio = (double)latin / visible.Length;
        return result.Confidence * 0.72 + readableRatio * 0.18 + (englishCandidate ? latinRatio * 0.1 : 0);
    }

    private static bool IsLatin(char character) =>
        character is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    private static bool IsCjk(char character) =>
        character is >= '\u3400' and <= '\u9FFF' or >= '\u3040' and <= '\u30FF' or >= '\uAC00' and <= '\uD7AF';

    public void Dispose()
    {
        lock (_sync)
        {
            if (_worker is { HasExited: false })
            {
                _worker.Kill(entireProcessTree: true);
                _worker.WaitForExit(2000);
            }

            _worker?.Dispose();
            _worker = null;
        }
    }

    private sealed record OcrWireResponse(
        string RequestId,
        bool Success,
        string Text,
        string? Error,
        long ElapsedMilliseconds,
        double Confidence);
}

internal sealed record OcrResult(
    string Text,
    long ElapsedMilliseconds,
    double Confidence,
    bool WasEnglishReviewed,
    bool UsedEnglishResult,
    bool WasEnhanced);
