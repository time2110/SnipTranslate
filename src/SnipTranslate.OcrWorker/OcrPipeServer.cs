using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace SnipTranslate.OcrWorker;

internal static class OcrPipeServer
{
    internal const string PipeName = "SnipTranslate.Ocr.v1";

    internal static async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        await using var engine = new OcrEngine();
        await engine.InitializeAsync();

        while (!cancellationToken.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                PipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            using var idleCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            idleCancellation.CancelAfter(TimeSpan.FromMinutes(10));
            try
            {
                await pipe.WaitForConnectionAsync(idleCancellation.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return 0;
            }
            using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, leaveOpen: true);
            await using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, leaveOpen: true)
            {
                AutoFlush = true
            };

            var json = await reader.ReadLineAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(json))
            {
                continue;
            }

            OcrResponse response;
            try
            {
                var request = JsonSerializer.Deserialize<OcrRequest>(json)
                    ?? throw new InvalidDataException("OCR 请求格式无效。");
                response = await engine.RecognizeAsync(request, cancellationToken);
            }
            catch (Exception exception)
            {
                response = new OcrResponse(string.Empty, false, string.Empty, exception.Message, 0, 0);
            }

            await writer.WriteLineAsync(JsonSerializer.Serialize(response));
        }

        return 0;
    }
}
