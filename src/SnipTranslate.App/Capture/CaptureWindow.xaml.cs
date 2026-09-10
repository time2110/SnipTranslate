using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SnipTranslate.Native;
using SnipTranslate.Ocr;
using SnipTranslate.Translation;

namespace SnipTranslate.Capture;

public partial class CaptureWindow : Window
{
    private const uint SwpShowWindow = 0x0040;
    private static readonly nint HwndTopmost = new(-1);

    private readonly CaptureFrame _frame;
    private readonly CaptureMode _mode;
    private readonly OcrClient _ocr;
    private readonly ITranslationProvider _translator;
    private readonly Action _closeAll;
    private readonly CaptureSurface _surface;
    private readonly CancellationTokenSource _operationCancellation = new();
    private CancellationTokenSource? _requestCancellation;
    private bool _operationRunning;
    private bool _showTranslation;
    private bool _changingLanguages;
    private string _lastOcrText = string.Empty;
    private AnnotationTool _activeTool;
    private Color _annotationColor = Color.FromRgb(239, 68, 68);
    private double _annotationThickness = 3;
    private TextBox? _textEditor;
    private CapturePointerAdorner? _pointerAdorner;

    internal CaptureWindow(
        CaptureFrame frame,
        CaptureMode mode,
        OcrClient ocr,
        ITranslationProvider translator,
        Action closeAll)
    {
        InitializeComponent();
        _frame = frame;
        _mode = mode;
        _ocr = ocr;
        _translator = translator;
        _closeAll = closeAll;
        _surface = new CaptureSurface(frame.Bitmap);
        SurfaceHost.Children.Add(_surface);

        ModeText.Text = mode == CaptureMode.Translate
            ? "翻译截图 · 框选后将自动识别"
            : "截图 · 拖动框选，双击直接复制";

        _surface.SelectionChanged += OnSelectionChanged;
        _surface.SelectionCompleted += OnSelectionCompleted;
        _surface.CopyRequested += (_, _) => CopyAndClose();
        _surface.TextRequested += OnTextRequested;
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += (_, _) =>
        {
            _requestCancellation?.Cancel();
            _operationCancellation.Cancel();
        };
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _surface.Focus();
        if (_pointerAdorner is null && AdornerLayer.GetAdornerLayer(Root) is { } layer)
        {
            _pointerAdorner = new CapturePointerAdorner(Root, _surface);
            layer.Add(_pointerAdorner);
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            HwndTopmost,
            _frame.Bounds.Left,
            _frame.Bounds.Top,
            _frame.Bounds.Width,
            _frame.Bounds.Height,
            SwpShowWindow);
    }

    private void PositionToolbar(Rect selection)
    {
        const double estimatedWidth = 500;
        const double height = 42;
        const double gap = 9;

        var left = Math.Clamp(selection.Left, 8, Math.Max(8, ActualWidth - estimatedWidth - 8));
        var top = selection.Bottom + gap;
        if (top + height > ActualHeight - 8)
        {
            top = Math.Max(8, selection.Top - height - gap);
        }

        Canvas.SetLeft(Toolbar, left);
        Canvas.SetTop(Toolbar, top);

        if (ToolOptionsPanel.Visibility == Visibility.Visible)
        {
            var optionsTop = top + height + 7;
            if (optionsTop + ToolOptionsPanel.Height > ActualHeight - 8)
            {
                optionsTop = Math.Max(8, top - ToolOptionsPanel.Height - 7);
            }

            Canvas.SetLeft(ToolOptionsPanel, left);
            Canvas.SetTop(ToolOptionsPanel, optionsTop);
        }

        if (ResultPanel.Visibility == Visibility.Visible)
        {
            PositionResultPanel(selection);
        }
    }

    private void PositionResultPanel(Rect selection)
    {
        const double gap = 12;
        var panelWidth = ResultPanel.Width;
        ResultPanel.Height = Math.Clamp(ActualHeight - 16, 260, 500);

        double left;
        if (selection.Right + gap + panelWidth <= ActualWidth - 8)
        {
            left = selection.Right + gap;
        }
        else if (selection.Left - gap - panelWidth >= 8)
        {
            left = selection.Left - gap - panelWidth;
        }
        else
        {
            left = Math.Clamp(selection.Right - panelWidth, 8, Math.Max(8, ActualWidth - panelWidth - 8));
        }

        var top = Math.Clamp(selection.Top, 8, Math.Max(8, ActualHeight - ResultPanel.Height - 8));
        Canvas.SetLeft(ResultPanel, left);
        Canvas.SetTop(ResultPanel, top);
    }

    private void OnSelectionChanged(object? sender, Rect selection)
    {
        PositionToolbar(selection);
        if (ResultPanel.Visibility == Visibility.Visible)
        {
            _requestCancellation?.Cancel();
            ResultPanel.Visibility = Visibility.Collapsed;
            _lastOcrText = string.Empty;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var key = EffectiveKey(e);
        if (_textEditor is not null)
        {
            if (key == Key.Escape)
            {
                e.Handled = true;
                CancelTextEditor();
            }
            else if (key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
            {
                e.Handled = true;
                CommitTextEditor();
            }

            return;
        }

        if (key == Key.Escape)
        {
            e.Handled = true;
            if (_surface.CancelTool())
            {
                SetAnnotationTool(AnnotationTool.Select);
                return;
            }

            if (ResultPanel.Visibility == Visibility.Visible)
            {
                ResultPanel.Visibility = Visibility.Collapsed;
                return;
            }
            _closeAll();
            return;
        }

        if (key == Key.C && Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
            ResultPanel.Visibility == Visibility.Visible)
        {
            e.Handled = true;
            CopyText(string.IsNullOrWhiteSpace(TranslatedText.Text) ? SourceText.Text : TranslatedText.Text);
            return;
        }

        if (key == Key.Enter)
        {
            e.Handled = true;
            CopyAndClose();
            return;
        }

        if (key == Key.Space)
        {
            e.Handled = true;
            PinAndClose();
            return;
        }

        if (key == Key.O)
        {
            e.Handled = true;
            _ = RunOcrAsync(translate: false);
            return;
        }

        if (key == Key.T)
        {
            e.Handled = true;
            _ = RunOcrAsync(translate: true);
            return;
        }

        if (key == Key.P)
        {
            e.Handled = true;
            SetAnnotationTool(AnnotationTool.Pen);
            return;
        }

        if (key == Key.R)
        {
            e.Handled = true;
            SetAnnotationTool(AnnotationTool.Rectangle);
            return;
        }

        if (key == Key.A)
        {
            e.Handled = true;
            SetAnnotationTool(AnnotationTool.Arrow);
            return;
        }

        if (key == Key.X)
        {
            e.Handled = true;
            SetAnnotationTool(AnnotationTool.Text);
            return;
        }

        if (key == Key.C &&
            Keyboard.Modifiers is ModifierKeys.None or ModifierKeys.Shift &&
            _activeTool == AnnotationTool.Select)
        {
            e.Handled = true;
            _surface.CopyCurrentColor();
            return;
        }

        if (key is Key.LeftShift or Key.RightShift && !e.IsRepeat && _activeTool == AnnotationTool.Select)
        {
            e.Handled = true;
            _surface.ToggleColorFormat();
            return;
        }

        if (key == Key.Z && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            _surface.Undo();
            return;
        }

        if (key == Key.S && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            e.Handled = true;
            SaveAndClose();
            return;
        }

        var step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? 10 : 1;
        var movement = key switch
        {
            Key.Left => (-step, 0),
            Key.Right => (step, 0),
            Key.Up => (0, -step),
            Key.Down => (0, step),
            _ => (0, 0)
        };

        if (movement != (0, 0))
        {
            e.Handled = true;
            _surface.MoveSelection(movement.Item1, movement.Item2);
        }
    }

    private void OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        // Color format changes persist after Shift is released.
    }

    private static Key EffectiveKey(KeyEventArgs e) => e.Key switch
    {
        Key.ImeProcessed => e.ImeProcessedKey,
        Key.System => e.SystemKey,
        _ => e.Key
    };

    private void OnWindowPreviewMouseMove(object sender, MouseEventArgs e)
    {
        var overInteractiveUi = Toolbar.IsMouseOver ||
                                ToolOptionsPanel.IsMouseOver ||
                                ResultPanel.IsMouseOver ||
                                _textEditor?.IsMouseOver == true;
        _surface.SetPointerOverlayVisible(!overInteractiveUi);
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e) =>
        _surface.SetPointerOverlayVisible(false);

    private void OnCopyClick(object sender, RoutedEventArgs e) => CopyAndClose();
    private void OnSaveClick(object sender, RoutedEventArgs e) => SaveAndClose();
    private void OnPinClick(object sender, RoutedEventArgs e) => PinAndClose();
    private void OnPenClick(object sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationTool.Pen);
    private void OnRectangleClick(object sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationTool.Rectangle);
    private void OnArrowClick(object sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationTool.Arrow);
    private void OnTextClick(object sender, RoutedEventArgs e) => SetAnnotationTool(AnnotationTool.Text);
    private void OnUndoClick(object sender, RoutedEventArgs e) => _surface.Undo();
    private void OnOcrClick(object sender, RoutedEventArgs e) => _ = RunOcrAsync(translate: false);
    private void OnCloseClick(object sender, RoutedEventArgs e) => _closeAll();
    private void OnCloseResultClick(object sender, RoutedEventArgs e) => ResultPanel.Visibility = Visibility.Collapsed;
    private void OnCopySourceClick(object sender, RoutedEventArgs e) => CopyText(SourceText.Text);
    private void OnCopyTranslationClick(object sender, RoutedEventArgs e) => CopyText(TranslatedText.Text);
    private void OnRetranslateClick(object sender, RoutedEventArgs e) => _ = RetranslateAsync();
    private void OnTranslateOcrClick(object sender, RoutedEventArgs e) => _ = RetranslateAsync();

    private void SetAnnotationTool(AnnotationTool tool)
    {
        _activeTool = tool;
        _surface.SetTool(tool);
        _surface.SetAnnotationStyle(_annotationColor, _annotationThickness);
        ToolOptionsPanel.Visibility = tool == AnnotationTool.Select ? Visibility.Collapsed : Visibility.Visible;
        TextOptions.Visibility = tool == AnnotationTool.Text ? Visibility.Visible : Visibility.Collapsed;

        var normal = Brushes.Transparent;
        var selected = new SolidColorBrush(Color.FromArgb(210, 186, 230, 253));
        PenButton.Background = tool == AnnotationTool.Pen ? selected : normal;
        RectangleButton.Background = tool == AnnotationTool.Rectangle ? selected : normal;
        ArrowButton.Background = tool == AnnotationTool.Arrow ? selected : normal;
        TextButton.Background = tool == AnnotationTool.Text ? selected : normal;

        if (_surface.Selection is { } selection)
        {
            PositionToolbar(selection);
        }
        _surface.Focus();
    }

    private void OnColorClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string colorText } &&
            ColorConverter.ConvertFromString(colorText) is Color color)
        {
            _annotationColor = color;
            ApplyAnnotationOptions();
        }
    }

    private void OnAnnotationOptionChanged(object sender, SelectionChangedEventArgs e) => ApplyAnnotationOptions();

    private void ApplyAnnotationOptions()
    {
        if (_surface is null)
        {
            return;
        }

        if ((ThicknessBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() is { } thicknessText &&
            double.TryParse(thicknessText, out var thickness))
        {
            _annotationThickness = thickness;
        }

        _surface.SetAnnotationStyle(_annotationColor, _annotationThickness);
        _surface.Focus();
    }

    private void OnTextRequested(object? sender, Point position)
    {
        CommitTextEditor();
        var fontSize = SelectedNumber(FontSizeBox, 16);
        var bold = BoldTextButton.IsChecked == true;
        var textOrigin = AlignTextOrigin(position, fontSize);
        _surface.BeginTextInput(textOrigin, fontSize, bold);
        var editor = new TextBox
        {
            Width = 2,
            Height = Math.Max(20, fontSize * 1.5),
            Padding = new Thickness(0),
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            FontSize = fontSize,
            FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
            Foreground = Brushes.Transparent,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = Brushes.Transparent,
            Opacity = 0.01
        };
        InputMethod.SetIsInputMethodEnabled(editor, true);
        editor.TextChanged += (_, _) => _surface.UpdateTextInput(editor.Text);
        editor.LostKeyboardFocus += (_, _) => CommitTextEditor();
        _textEditor = editor;
        OverlayCanvas.Children.Add(editor);
        Canvas.SetLeft(editor, Math.Clamp(textOrigin.X, 0, Math.Max(0, ActualWidth - editor.Width)));
        Canvas.SetTop(editor, Math.Clamp(textOrigin.Y, 0, Math.Max(0, ActualHeight - editor.Height)));
        Panel.SetZIndex(editor, 100);
        editor.Focus();
        Keyboard.Focus(editor);
    }

    private Point AlignTextOrigin(Point clickPosition, double fontSize)
    {
        // The system I-beam cursor's hot spot is vertically centered. Drawing text at
        // that Y treats it as the top edge and makes the glyphs appear too low.
        var x = clickPosition.X + 1;
        var y = clickPosition.Y - fontSize * 0.58;
        if (_surface.Selection is { } selection)
        {
            x = Math.Clamp(x, selection.Left + 1, Math.Max(selection.Left + 1, selection.Right - 2));
            y = Math.Clamp(y, selection.Top + 1, Math.Max(selection.Top + 1, selection.Bottom - fontSize * 1.3));
        }

        return new Point(x, y);
    }

    private void CommitTextEditor()
    {
        if (_textEditor is not { } editor)
        {
            return;
        }

        _textEditor = null;
        OverlayCanvas.Children.Remove(editor);
        _surface.CommitTextInput();
        _surface.Focus();
    }

    private void CancelTextEditor()
    {
        if (_textEditor is not { } editor)
        {
            return;
        }

        _textEditor = null;
        OverlayCanvas.Children.Remove(editor);
        _surface.CancelTextInput();
        _surface.Focus();
    }

    private static double SelectedNumber(ComboBox comboBox, double fallback) =>
        double.TryParse((comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var value) ? value : fallback;

    private void CopyAndClose()
    {
        var image = _surface.ExportSelection();
        if (image is null)
        {
            return;
        }

        Clipboard.SetImage(image);
        _closeAll();
    }

    private void SaveAndClose()
    {
        var image = _surface.ExportSelection();
        if (image is null)
        {
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "保存截图",
            Filter = "PNG 图片|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            FileName = $"SnipTranslate_{DateTime.Now:yyyyMMdd_HHmmss}.png"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        using var stream = File.Create(dialog.FileName);
        encoder.Save(stream);
        _closeAll();
    }

    private void PinAndClose()
    {
        var image = _surface.ExportSelection();
        if (image is null || _surface.Selection is not { } selection)
        {
            return;
        }

        var pin = new PinWindow(image)
        {
            Left = Left + selection.Left,
            Top = Top + selection.Top
        };
        pin.Show();
        _closeAll();
    }

    private void OnSelectionCompleted(object? sender, EventArgs e)
    {
        Toolbar.Visibility = Visibility.Visible;
        if (_mode == CaptureMode.Translate)
        {
            _ = RunOcrAsync(translate: true);
        }
    }

    private async Task RunOcrAsync(bool translate)
    {
        if (_surface.ExportSelection() is not { } image || _surface.Selection is not { } selection)
        {
            return;
        }

        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_operationCancellation.Token);
        var cancellationToken = _requestCancellation.Token;
        _operationRunning = true;
        _showTranslation = translate;
        ResultPanel.Visibility = Visibility.Visible;
        PositionResultPanel(selection);
        SetTranslationVisibility(translate);
        SourceText.Text = "正在识别…";
        TranslatedText.Text = translate ? "等待 OCR 结果…" : string.Empty;
        TimingText.Text = string.Empty;
        ResultStatusText.Text = "本地 OCR 处理中";
        RetranslateButton.IsEnabled = false;
        ModeBadge.Visibility = Visibility.Visible;
        ModeText.Text = "正在本地识别…";

        try
        {
            var ocrResult = await _ocr.RecognizeAsync(image, cancellationToken);
            _lastOcrText = ocrResult.Text;
            SourceText.Text = ocrResult.Text;
            TimingText.Text = $"{ocrResult.ElapsedMilliseconds} ms";
            ResultStatusText.Text = string.IsNullOrWhiteSpace(ocrResult.Text) ? "未识别到文字" : "识别完成";
            RetranslateButton.IsEnabled = !string.IsNullOrWhiteSpace(ocrResult.Text);
            if (translate)
            {
                ModeText.Text = "正在通过 Google 翻译…";
                await TranslateCurrentTextAsync(cancellationToken);
            }

            ModeText.Text = translate ? "翻译完成" : "OCR 完成";
        }
        catch (OperationCanceledException)
        {
            // Closing a capture intentionally cancels in-flight work.
        }
        catch (Exception exception)
        {
            ModeText.Text = $"失败：{exception.Message}";
            ResultStatusText.Text = $"失败：{exception.Message}";
            if (string.IsNullOrWhiteSpace(_lastOcrText))
            {
                SourceText.Text = exception.Message;
            }
        }
        finally
        {
            _operationRunning = false;
        }
    }

    private async Task RetranslateAsync()
    {
        if (_operationRunning || string.IsNullOrWhiteSpace(SourceText.Text))
        {
            return;
        }

        _lastOcrText = SourceText.Text.Trim();
        _showTranslation = true;
        SetTranslationVisibility(true);
        _requestCancellation?.Cancel();
        _requestCancellation?.Dispose();
        _requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(_operationCancellation.Token);
        _operationRunning = true;
        try
        {
            await TranslateCurrentTextAsync(_requestCancellation.Token);
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            ResultStatusText.Text = $"翻译失败：{exception.Message}";
            TranslatedText.Text = exception.Message;
        }
        finally
        {
            _operationRunning = false;
        }
    }

    private async Task TranslateCurrentTextAsync(CancellationToken cancellationToken)
    {
        var text = SourceText.Text.Trim();
        if (string.IsNullOrEmpty(text))
        {
            TranslatedText.Text = string.Empty;
            return;
        }

        var source = SelectedLanguage(SourceLanguageBox, "auto");
        var requestedTarget = SelectedLanguage(TargetLanguageBox, "bilingual");
        var detected = DetectPrimaryLanguage(text);
        var target = requestedTarget == "bilingual"
            ? (source == "en" || source == "auto" && detected == "en" ? "zh-CN" : "en")
            : requestedTarget;

        TranslatedText.Text = "正在翻译…";
        ResultStatusText.Text = $"Google · {LanguageLabel(source == "auto" ? detected : source)} → {LanguageLabel(target)}";
        var translated = await _translator.TranslateAsync(text, source, target, cancellationToken);
        TranslatedText.Text = translated;
        ResultStatusText.Text = $"完成 · {LanguageLabel(source == "auto" ? detected : source)} → {LanguageLabel(target)}";
    }

    private async void OnLanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_changingLanguages || !IsLoaded || !_showTranslation || ResultPanel.Visibility != Visibility.Visible ||
            string.IsNullOrWhiteSpace(_lastOcrText))
        {
            return;
        }

        await RetranslateAsync();
    }

    private void OnSwapLanguageClick(object sender, RoutedEventArgs e)
    {
        var detected = DetectPrimaryLanguage(SourceText.Text);
        var source = SelectedLanguage(SourceLanguageBox, "auto");
        var target = SelectedLanguage(TargetLanguageBox, "bilingual");
        source = source == "auto" ? detected : source;
        target = target == "bilingual" ? (source == "en" ? "zh-CN" : "en") : target;
        _changingLanguages = true;
        try
        {
            SelectLanguage(SourceLanguageBox, target);
            SelectLanguage(TargetLanguageBox, source);
        }
        finally
        {
            _changingLanguages = false;
        }

        _ = RetranslateAsync();
    }

    private void SetTranslationVisibility(bool visible)
    {
        var visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        TranslationHeading.Visibility = visibility;
        TranslatedText.Visibility = visibility;
        RetranslateButton.Visibility = visibility;
        CopyTranslationButton.Visibility = visibility;
        TranslateOcrButton.Visibility = visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private static string SelectedLanguage(ComboBox comboBox, string fallback) =>
        (comboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;

    private static void SelectLanguage(ComboBox comboBox, string language)
    {
        comboBox.SelectedItem = comboBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), language, StringComparison.OrdinalIgnoreCase));
    }

    private static string DetectPrimaryLanguage(string text)
    {
        var chinese = text.Count(character => character is >= '\u3400' and <= '\u9FFF');
        var latin = text.Count(character => character is >= 'A' and <= 'Z' or >= 'a' and <= 'z');
        return chinese >= latin ? "zh-CN" : "en";
    }

    private static string LanguageLabel(string language) => language switch
    {
        "zh-CN" => "中文",
        "en" => "English",
        "ja" => "日本語",
        "ko" => "한국어",
        _ => "自动识别"
    };

    private static void CopyText(string text)
    {
        if (!string.IsNullOrWhiteSpace(text))
        {
            Clipboard.SetText(text);
        }
    }
}
