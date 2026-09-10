using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace SnipTranslate.Capture;

public partial class PinWindow : Window
{
    private readonly double _naturalWidth;
    private readonly double _naturalHeight;
    private double _scale = 1;

    internal PinWindow(BitmapSource image)
    {
        InitializeComponent();
        PinnedImage.Source = image;
        _naturalWidth = image.Width;
        _naturalHeight = image.Height;
        ApplyScale();
    }

    private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            _scale = 1;
            ApplyScale();
            return;
        }

        DragMove();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            Opacity = Math.Clamp(Opacity + (e.Delta > 0 ? 0.08 : -0.08), 0.2, 1);
            return;
        }

        _scale = Math.Clamp(_scale * (e.Delta > 0 ? 1.1 : 0.9), 0.2, 5);
        ApplyScale();
    }

    private void OnMouseRightButtonUp(object sender, MouseButtonEventArgs e) => Close();

    private void ApplyScale()
    {
        PinnedImage.Width = _naturalWidth * _scale;
        PinnedImage.Height = _naturalHeight * _scale;
    }
}
