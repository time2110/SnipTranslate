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
        var quality = NormalizeQuality(request.Quality);
        var engine = await GetEngineAsync(request.Language, quality);
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var imageBytes = File.ReadAllBytes(request.ImagePath);
            var result = engine.Detect(
                imageBytes,
                padding: request.Enhanced ? 20 : 12,
                maxSideLen: quality == "accurate" || request.Enhanced ? 2400 : 1600,
                boxScoreThresh: request.Enhanced ? 0.36f : 0.5f,
                boxThresh: request.Enhanced ? 0.2f : 0.3f,
                unClipRatio: request.Enhanced ? 1.8f : 1.6f,
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
                stopwatch.ElapsedMilliseconds,
                CalculateConfidence(result?.TextBlocks));
        }, cancellationToken);
    }

    private async Task<OcrLite> GetEngineAsync(string? language, string quality = "fast")
    {
        var profile = NormalizeProfile(language);
        var cacheKey = $"{quality}:{profile}";
        if (_engines.TryGetValue(cacheKey, out var existing)) return existing;

        var accurate = quality == "accurate" && profile != "ko";
        var detectionModel = accurate ? AccurateDetectionModel : RequiredModelNames[0];
        var (recognitionModel, dictionary) = accurate
            ? (AccurateRecognitionModel, RequiredModelNames[3])
            : profile switch
        {
            "ko" => ("korean_PP-OCRv5_rec_mobile.onnx", "ppocrv5_korean_dict.txt"),
            "en" when File.Exists(Path.Combine(_modelDirectory, "en_PP-OCRv5_rec_mobile.onnx")) =>
                ("en_PP-OCRv5_rec_mobile.onnx", "ppocrv5_en_dict.txt"),
            _ => (RequiredModelNames[2], RequiredModelNames[3])
        };
        if (!File.Exists(Path.Combine(_modelDirectory, detectionModel)) ||
            !File.Exists(Path.Combine(_modelDirectory, recognitionModel)) ||
            !File.Exists(Path.Combine(_modelDirectory, dictionary)))
        {
            var mode = accurate ? "Server 准确" : LanguageLabel(profile);
            throw new FileNotFoundException($"缺少 {mode} OCR 模型，请运行 scripts/Get-RapidOcrModels.ps1。");
        }

        var engine = new OcrLite
        {
            DetPath = Path.Combine(_modelDirectory, detectionModel),
            ClsPath = Path.Combine(_modelDirectory, RequiredModelNames[1]),
            RecPath = Path.Combine(_modelDirectory, recognitionModel),
            KeyDicPath = Path.Combine(_modelDirectory, dictionary)
        };
        await engine.InitModels();
        _engines[cacheKey] = engine;
        return engine;
    }

    private static double CalculateConfidence(IEnumerable<RapidOCRLib.Models.TextBlock>? blocks)
    {
        if (blocks is null) return 0;
        var characterScores = blocks.SelectMany(block => block.CharScores ?? []).Select(score => (double)score).ToArray();
        if (characterScores.Length > 0) return characterScores.Average();
        var boxScores = blocks.Select(block => (double)block.BoxScore).Where(score => score > 0).ToArray();
        return boxScores.Length == 0 ? 0 : boxScores.Average();
    }

    private static string NormalizeProfile(string? language) => language?.ToLowerInvariant() switch
    {
        "ko" => "ko",
        "en" => "en",
        _ => "auto"
    };

    private static string NormalizeQuality(string? quality) =>
        string.Equals(quality, "accurate", StringComparison.OrdinalIgnoreCase) ? "accurate" : "fast";

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

    private const string AccurateDetectionModel = "ch_PP-OCRv5_det_server.onnx";
    private const string AccurateRecognitionModel = "ch_PP-OCRv5_rec_server.onnx";
}
