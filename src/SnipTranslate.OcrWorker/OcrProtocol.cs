namespace SnipTranslate.OcrWorker;

internal sealed record OcrRequest(
    string RequestId,
    string ImagePath,
    string Language = "auto",
    bool EnableAngleDetection = true,
    string Quality = "fast",
    bool Enhanced = false);

internal sealed record OcrResponse(
    string RequestId,
    bool Success,
    string Text,
    string? Error,
    long ElapsedMilliseconds,
    double Confidence = 0);
