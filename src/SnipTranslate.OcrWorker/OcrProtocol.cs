namespace SnipTranslate.OcrWorker;

internal sealed record OcrRequest(string RequestId, string ImagePath);

internal sealed record OcrResponse(
    string RequestId,
    bool Success,
    string Text,
    string? Error,
    long ElapsedMilliseconds);

