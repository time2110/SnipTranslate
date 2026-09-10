namespace SnipTranslate.Translation;

internal interface ITranslationProvider
{
    Task<string> TranslateAsync(
        string text,
        string sourceLanguage,
        string targetLanguage,
        CancellationToken cancellationToken);
}
