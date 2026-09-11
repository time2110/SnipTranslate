using System.Windows;
using SnipTranslate.Services;

namespace SnipTranslate.Translation;

public partial class ResultWindow : Window
{
    internal ResultWindow(string source, string? translated, long ocrMilliseconds)
    {
        InitializeComponent();
        SourceText.Text = source;
        TranslatedText.Text = translated ?? string.Empty;
        TranslationHeading.Visibility = translated is null ? Visibility.Collapsed : Visibility.Visible;
        TranslatedText.Visibility = translated is null ? Visibility.Collapsed : Visibility.Visible;
        TimingText.Text = $"OCR {ocrMilliseconds} ms";
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        ClipboardService.SetText(string.IsNullOrWhiteSpace(TranslatedText.Text)
            ? SourceText.Text
            : TranslatedText.Text);
        Close();
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
