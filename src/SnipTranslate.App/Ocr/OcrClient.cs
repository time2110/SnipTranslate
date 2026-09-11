using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Media.Imaging;

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
        CancellationToken cancellationToken)
    {
        Prepare();
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
                EnableAngleDetection = enableAngleDetection
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

            return new OcrResult(response.Text, response.ElapsedMilliseconds);
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
        long ElapsedMilliseconds);
}

internal sealed record OcrResult(string Text, long ElapsedMilliseconds);
