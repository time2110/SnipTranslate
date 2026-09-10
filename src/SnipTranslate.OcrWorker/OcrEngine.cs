using System.Diagnostics;
using RapidOCRLib;

namespace SnipTranslate.OcrWorker;

internal sealed class OcrEngine : IAsyncDisposable
{
    private readonly string _modelDirectory = Path.Combine(AppContext.BaseDirectory, "models");
    private OcrLite? _engine;

    internal static bool ModelsPresent()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "models");
        return RequiredModelNames.All(name => File.Exists(Path.Combine(path, name)));
    }

    internal async Task InitializeAsync()
    {
        if (!ModelsPresent())
        {
            throw new FileNotFoundException("RapidOCR 模型不完整，请安装默认中英模型包。");
        }

        _engine = new OcrLite
        {
            DetPath = Path.Combine(_modelDirectory, RequiredModelNames[0]),
            ClsPath = Path.Combine(_modelDirectory, RequiredModelNames[1]),
            RecPath = Path.Combine(_modelDirectory, RequiredModelNames[2]),
            KeyDicPath = Path.Combine(_modelDirectory, RequiredModelNames[3])
        };
        await _engine.InitModels();
    }

    internal Task<OcrResponse> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        if (_engine is null)
        {
            throw new InvalidOperationException("OCR 引擎尚未初始化。");
        }

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var imageBytes = File.ReadAllBytes(request.ImagePath);
            var result = _engine.Detect(
                imageBytes,
                padding: 12,
                maxSideLen: 1600,
                boxScoreThresh: 0.5f,
                boxThresh: 0.3f,
                unClipRatio: 1.6f,
                doAngle: false,
                mostAngle: false);
            stopwatch.Stop();

            return new OcrResponse(
                request.RequestId,
                true,
                result is null
                    ? string.Empty
                    : TextLayoutReflow.Arrange(result.TextBlocks, result.StrRes),
                null,
                stopwatch.ElapsedMilliseconds);
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        if (_engine is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _engine = null;
        return ValueTask.CompletedTask;
    }

    private static readonly string[] RequiredModelNames =
    [
        "ch_PP-OCRv5_mobile_det.onnx",
        "ch_ppocr_mobile_v2.0_cls_infer.onnx",
        "ch_PP-OCRv5_rec_mobile_infer.onnx",
        "ppocrv5_dict.txt"
    ];
}
