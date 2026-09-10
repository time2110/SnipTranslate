using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace SnipTranslate.Capture;

internal sealed class CapturePointerAdorner : Adorner
{
    private readonly CaptureSurface _surface;

    internal CapturePointerAdorner(UIElement adornedElement, CaptureSurface surface)
        : base(adornedElement)
    {
        _surface = surface;
        IsHitTestVisible = false;
        _surface.PointerOverlayChanged += OnPointerOverlayChanged;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        _surface.RenderPointerOverlay(drawingContext);
    }

    private void OnPointerOverlayChanged(object? sender, EventArgs e) => InvalidateVisual();
}
