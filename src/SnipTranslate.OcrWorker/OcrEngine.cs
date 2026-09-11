using System.Diagnostics;
using RapidOCRLib;

namespace SnipTranslate.OcrWorker;

internal sealed class OcrEngine : IAsyncDisposable
{
    private readonly string _modelDirectory = Path.Combine(AppContext.BaseDirectory, "models");
    private readonly Dictionary<string, OcrLite> _engines = new(StringComparer.OrdinalIgnoreCase);

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

        await GetEngineAsync("auto");
    }

    internal Task<OcrResponse> RecognizeAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        return RecognizeCoreAsync(request, cancellationToken);
    }

    private async Task<OcrResponse> RecognizeCoreAsync(OcrRequest request, CancellationToken cancellationToken)
    {
        var engine = await GetEngineAsync(request.Language);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var imageBytes = File.ReadAllBytes(request.ImagePath);
            var result = engine.Detect(
                imageBytes,
                padding: 12,
                maxSideLen: 1600,
                boxScoreThresh: 0.5f,
                boxThresh: 0.3f,
                unClipRatio: 1.6f,
                doAngle: request.EnableAngleDetection,
                mostAngle: request.EnableAngleDetection);
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

    private async Task<OcrLite> GetEngineAsync(string? language)
    {
        var profile = NormalizeProfile(language);
        if (_engines.TryGetValue(profile, out var existing)) return existing;

        var (recognitionModel, dictionary) = profile switch
        {
            "ko" => ("korean_PP-OCRv5_rec_mobile.onnx", "ppocrv5_korean_dict.txt"),
            "en" when File.Exists(Path.Combine(_modelDirectory, "en_PP-OCRv5_rec_mobile.onnx")) =>
                ("en_PP-OCRv5_rec_mobile.onnx", "ppocrv5_en_dict.txt"),
            _ => (RequiredModelNames[2], RequiredModelNames[3])
        };
        if (!File.Exists(Path.Combine(_modelDirectory, recognitionModel)) ||
            !File.Exists(Path.Combine(_modelDirectory, dictionary)))
        {
            throw new FileNotFoundException($"缺少 {LanguageLabel(profile)} OCR 模型，请运行 scripts/Get-RapidOcrModels.ps1。");
        }

        var engine = new OcrLite
        {
            DetPath = Path.Combine(_modelDirectory, RequiredModelNames[0]),
            ClsPath = Path.Combine(_modelDirectory, RequiredModelNames[1]),
            RecPath = Path.Combine(_modelDirectory, recognitionModel),
            KeyDicPath = Path.Combine(_modelDirectory, dictionary)
        };
        await engine.InitModels();
        _engines[profile] = engine;
        return engine;
    }

    private static string NormalizeProfile(string? language) => language?.ToLowerInvariant() switch
    {
        "ko" => "ko",
        "en" => "en",
        _ => "auto"
    };

    private static string LanguageLabel(string profile) => profile switch
    {
        "ko" => "韩文",
        "en" => "英文",
        _ => "中英日"
    };

    public ValueTask DisposeAsync()
    {
        foreach (var disposable in _engines.Values.OfType<IDisposable>())
        {
            disposable.Dispose();
        }
        _engines.Clear();
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
