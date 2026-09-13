using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SnipTranslate.Services;
using DrawingRectangle = System.Drawing.Rectangle;

namespace SnipTranslate.Capture;

internal sealed class CaptureSurface : FrameworkElement
{
    private const double HandleSize = 7;
    private readonly BitmapSource _bitmap;
    private readonly DrawingRectangle _displayBounds;
    private readonly WindowTargetService _windowTargets;
    private Point _anchor;
    private Point _pointer;
    private Rect _startSelection;
    private DragOperation _dragOperation;
    private bool _dragging;
    private readonly List<IAnnotation> _annotations = [];
    private IAnnotation? _draftAnnotation;
    private AnnotationTool _activeTool;
    private Color _annotationColor = Color.FromRgb(239, 68, 68);
    private double _annotationThickness = 3;
    private string? _copiedColorMessage;
    private bool _showRgbColor;
    private TextAnnotation? _textDraft;
    private bool _pointerOverlayVisible = true;
    private Rect? _hoverTarget;
    private Rect? _pressedTarget;
    private int _snapLevel;

    internal CaptureSurface(BitmapSource bitmap, DrawingRectangle displayBounds, WindowTargetService windowTargets)
    {
        _bitmap = bitmap;
        _displayBounds = displayBounds;
        _windowTargets = windowTargets;
        Focusable = true;
        Cursor = Cursors.Cross;
        SnapsToDevicePixels = true;
        InputMethod.SetIsInputMethodEnabled(this, false);
    }

    internal Rect? Selection { get; private set; }
    internal bool HasSelection => Selection is { Width: >= 1, Height: >= 1 };

    internal event EventHandler<Rect>? SelectionChanged;
    internal event EventHandler? SelectionCompleted;
    internal event EventHandler? CopyRequested;
    internal event EventHandler<Point>? TextRequested;
    internal event EventHandler? PointerOverlayChanged;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _pointer = Constrain(e.GetPosition(this));

        if (e.ClickCount == 2 && HasSelection && Selection!.Value.Contains(_pointer))
        {
            CopyRequested?.Invoke(this, EventArgs.Empty);
            return;
        }

        _anchor = _pointer;
        _startSelection = Selection ?? Rect.Empty;

        if (_activeTool == AnnotationTool.Text && Selection is { } textBounds && textBounds.Contains(_pointer))
        {
            TextRequested?.Invoke(this, _pointer);
            return;
        }

        if (_activeTool != AnnotationTool.Select && Selection is { } annotationBounds && annotationBounds.Contains(_pointer))
        {
            _draftAnnotation = CreateAnnotation(_activeTool, _pointer, _annotationColor, _annotationThickness);
            _dragging = true;
            CaptureMouse();
            InvalidateVisual();
            return;
        }

        _dragOperation = HitTestOperation(_pointer);

        if (_dragOperation == DragOperation.None)
        {
            _pressedTarget = FindSnapTarget(_pointer);
            _dragOperation = DragOperation.New;
            _annotations.Clear();
            Selection = new Rect(_anchor, _anchor);
        }

        _dragging = true;
        CaptureMouse();
        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _pointer = Constrain(e.GetPosition(this));

        if (_dragging)
        {
            if (_draftAnnotation is not null)
            {
                _draftAnnotation.Update(_pointer);
            }
            else
            {
                Selection = ApplyDrag(_pointer);
                RaiseSelectionChanged();
            }
        }
        else
        {
            if (_activeTool == AnnotationTool.Select)
            {
                Cursor = CursorFor(HitTestOperation(_pointer));
                if (Selection is null) UpdateHoverTarget();
            }
        }

        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (_activeTool != AnnotationTool.Select || _dragging || Selection is not null)
        {
            return;
        }

        _snapLevel = Math.Clamp(_snapLevel + (e.Delta < 0 ? 1 : -1), 0, 2);
        UpdateHoverTarget();
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        if (_draftAnnotation is not null)
        {
            _draftAnnotation.Update(_pointer);
            if (_draftAnnotation.IsMeaningful)
            {
                _annotations.Add(_draftAnnotation);
            }

            _draftAnnotation = null;
            InvalidateVisual();
            return;
        }

        if (Selection is { Width: < 2 } or { Height: < 2 })
        {
            Selection = _pressedTarget;
            _pressedTarget = null;
            if (Selection is not null)
            {
                RaiseSelectionChanged();
                SelectionCompleted?.Invoke(this, EventArgs.Empty);
            }
        }
        else
        {
            SelectionCompleted?.Invoke(this, EventArgs.Empty);
        }

        _pressedTarget = null;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (Selection is null)
        {
            _hoverTarget = null;
            InvalidateVisual();
        }
    }

    internal void MoveSelection(double deltaX, double deltaY)
    {
        if (Selection is not { } selection)
        {
            return;
        }

        var x = Math.Clamp(selection.X + deltaX, 0, Math.Max(0, ActualWidth - selection.Width));
        var y = Math.Clamp(selection.Y + deltaY, 0, Math.Max(0, ActualHeight - selection.Height));
        Selection = new Rect(x, y, selection.Width, selection.Height);
        RaiseSelectionChanged();
        InvalidateVisual();
    }

    internal void SetTool(AnnotationTool tool)
    {
        _activeTool = tool;
        Cursor = tool switch
        {
            AnnotationTool.Pen => Cursors.Pen,
            AnnotationTool.Text => Cursors.IBeam,
            _ => Cursors.Cross
        };
        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void SetAnnotationStyle(Color color, double thickness)
    {
        _annotationColor = color;
        _annotationThickness = thickness;
        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RefreshOverlay()
    {
        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void RefreshSnapTarget()
    {
        if (Selection is null && _activeTool == AnnotationTool.Select)
        {
            UpdateHoverTarget();
            InvalidateVisual();
        }
    }

    internal void SetPointerOverlayVisible(bool visible)
    {
        if (_pointerOverlayVisible == visible)
        {
            return;
        }

        _pointerOverlayVisible = visible;
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void BeginTextInput(Point position, double fontSize, bool bold)
    {
        _textDraft = new TextAnnotation(position, string.Empty, fontSize, _annotationColor, bold, editing: true);
        InvalidateVisual();
    }

    internal void UpdateTextInput(string text)
    {
        _textDraft?.SetText(text);
        InvalidateVisual();
    }

    internal void CommitTextInput()
    {
        if (_textDraft is { IsMeaningful: true } draft)
        {
            draft.FinishEditing();
            _annotations.Add(draft);
        }

        _textDraft = null;
        InvalidateVisual();
    }

    internal void CancelTextInput()
    {
        _textDraft = null;
        InvalidateVisual();
    }

    internal void ToggleColorFormat()
    {
        _showRgbColor = !_showRgbColor;
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void CopyCurrentColor()
    {
        var color = CurrentPixelColor();
        var value = _showRgbColor
            ? $"{color.R}, {color.G}, {color.B}"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        ClipboardService.SetText(value);
        _copiedColorMessage = $"已复制 {value}";
        InvalidateVisual();
        PointerOverlayChanged?.Invoke(this, EventArgs.Empty);

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _copiedColorMessage = null;
            InvalidateVisual();
            PointerOverlayChanged?.Invoke(this, EventArgs.Empty);
        };
        timer.Start();
    }

    internal bool CancelTool()
    {
        if (_activeTool == AnnotationTool.Select)
        {
            return false;
        }

        SetTool(AnnotationTool.Select);
        return true;
    }

    internal void Undo()
    {
        if (_annotations.Count == 0)
        {
            return;
        }

        _annotations.RemoveAt(_annotations.Count - 1);
        InvalidateVisual();
    }

    internal BitmapSource? ExportSelection()
    {
        if (Selection is not { } selection || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var scaleX = _bitmap.PixelWidth / ActualWidth;
        var scaleY = _bitmap.PixelHeight / ActualHeight;
        var x = Math.Clamp((int)Math.Round(selection.X * scaleX), 0, _bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(selection.Y * scaleY), 0, _bitmap.PixelHeight - 1);
        var width = Math.Clamp((int)Math.Round(selection.Width * scaleX), 1, _bitmap.PixelWidth - x);
        var height = Math.Clamp((int)Math.Round(selection.Height * scaleY), 1, _bitmap.PixelHeight - y);
        var cropped = new CroppedBitmap(_bitmap, new Int32Rect(x, y, width, height));
        cropped.Freeze();
        if (_annotations.Count == 0)
        {
            return cropped;
        }

        var visual = new DrawingVisual();
        using (var context = visual.RenderOpen())
        {
            context.DrawImage(cropped, new Rect(0, 0, width, height));
            var matrix = Matrix.Identity;
            matrix.Translate(-selection.X, -selection.Y);
            matrix.Scale(scaleX, scaleY);
            context.PushTransform(new MatrixTransform(matrix));
            foreach (var annotation in _annotations)
            {
                annotation.Draw(context);
            }
            context.Pop();
        }

        var rendered = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rendered.Render(visual);
        rendered.Freeze();
        return rendered;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        drawingContext.DrawImage(_bitmap, bounds);

        var visibleSelection = Selection ?? _hoverTarget;
        if (visibleSelection is null)
        {
            drawingContext.DrawRectangle(new SolidColorBrush(Color.FromArgb(105, 0, 0, 0)), null, bounds);
            return;
        }
        var selection = visibleSelection.Value;

        var shade = new SolidColorBrush(Color.FromArgb(115, 0, 0, 0));
        drawingContext.DrawRectangle(shade, null, new Rect(0, 0, ActualWidth, selection.Top));
        drawingContext.DrawRectangle(shade, null, new Rect(0, selection.Bottom, ActualWidth, Math.Max(0, ActualHeight - selection.Bottom)));
        drawingContext.DrawRectangle(shade, null, new Rect(0, selection.Top, selection.Left, selection.Height));
        drawingContext.DrawRectangle(shade, null, new Rect(selection.Right, selection.Top, Math.Max(0, ActualWidth - selection.Right), selection.Height));

        var accent = new SolidColorBrush(Color.FromRgb(56, 189, 248));
        drawingContext.DrawRectangle(null, new Pen(accent, Selection is null ? 1 : 1.5), selection);

        if (Selection is null)
        {
            DrawSnapLevelLabel(drawingContext, selection);
            return;
        }

        drawingContext.PushClip(new RectangleGeometry(selection));
        foreach (var annotation in _annotations)
        {
            annotation.Draw(drawingContext);
        }
        _draftAnnotation?.Draw(drawingContext);
        _textDraft?.Draw(drawingContext);
        drawingContext.Pop();

        foreach (var point in HandlePoints(selection))
        {
            drawingContext.DrawRectangle(Brushes.White, new Pen(accent, 1.5), new Rect(
                point.X - HandleSize / 2,
                point.Y - HandleSize / 2,
                HandleSize,
                HandleSize));
        }

        DrawSizeLabel(drawingContext, selection);
    }

    private void UpdateHoverTarget()
    {
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
        {
            _hoverTarget = null;
            return;
        }

        _hoverTarget = FindSnapTarget(_pointer);
        if (_snapLevel != 0) return;
        var global = GlobalPoint(_pointer);
        _windowTargets.WarmControlsAt(global, () => Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Selection is null && _activeTool == AnnotationTool.Select &&
                !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                _hoverTarget = FindSnapTarget(_pointer);
                InvalidateVisual();
            }
        })));
    }

    private Rect? FindSnapTarget(Point point)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return null;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)) return null;
        if (_snapLevel == 2) return new Rect(0, 0, ActualWidth, ActualHeight);

        var global = GlobalPoint(point);
        var window = _windowTargets.WindowAt(global);
        if (window is null) return null;
        var target = window.Bounds;
        if (_snapLevel == 0 && _windowTargets.TryGetControls(window.Handle, out var controls))
        {
            target = controls.FirstOrDefault(rectangle => rectangle.Contains(global));
            if (target.Width <= 0 || target.Height <= 0) target = window.Bounds;
        }

        return ToLocalRect(target);
    }

    private System.Drawing.Point GlobalPoint(Point point) => new(
        _displayBounds.Left + (int)Math.Round(point.X * _bitmap.PixelWidth / Math.Max(1, ActualWidth)),
        _displayBounds.Top + (int)Math.Round(point.Y * _bitmap.PixelHeight / Math.Max(1, ActualHeight)));

    private Rect? ToLocalRect(DrawingRectangle target)
    {
        var clipped = DrawingRectangle.Intersect(target, _displayBounds);
        if (clipped.Width <= 0 || clipped.Height <= 0) return null;
        return new Rect(
            (clipped.Left - _displayBounds.Left) * ActualWidth / _bitmap.PixelWidth,
            (clipped.Top - _displayBounds.Top) * ActualHeight / _bitmap.PixelHeight,
            clipped.Width * ActualWidth / _bitmap.PixelWidth,
            clipped.Height * ActualHeight / _bitmap.PixelHeight);
    }

    private void DrawSnapLevelLabel(DrawingContext context, Rect selection)
    {
        var label = _snapLevel switch { 0 => "控件", 1 => "窗口", _ => "屏幕" };
        var formatted = new FormattedText(
            label,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            12,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var x = Math.Clamp(selection.Left + 5, 4, Math.Max(4, ActualWidth - formatted.Width - 16));
        var y = Math.Clamp(selection.Top + 5, 4, Math.Max(4, ActualHeight - formatted.Height - 12));
        var background = new Rect(x - 5, y - 3, formatted.Width + 10, formatted.Height + 6);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(210, 15, 23, 42)), null, background, 4, 4);
        context.DrawText(formatted, new Point(x, y));
    }

    internal void RenderPointerOverlay(DrawingContext drawingContext)
    {
        if (_pointerOverlayVisible && _activeTool == AnnotationTool.Select)
        {
            DrawMagnifier(drawingContext);
        }
    }

    private void DrawSizeLabel(DrawingContext context, Rect selection)
    {
        var scaleX = _bitmap.PixelWidth / Math.Max(1, ActualWidth);
        var scaleY = _bitmap.PixelHeight / Math.Max(1, ActualHeight);
        var text = $"{Math.Round(selection.Width * scaleX):0} × {Math.Round(selection.Height * scaleY):0} px";
        var formatted = new FormattedText(
            text,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"),
            13,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);

        var x = selection.Left;
        var y = selection.Top >= 30 ? selection.Top - 27 : selection.Top + 7;
        var background = new Rect(x, y, formatted.Width + 14, formatted.Height + 8);
        context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(220, 31, 41, 55)), null, background, 4, 4);
        context.DrawText(formatted, new Point(x + 7, y + 4));
    }

    private void DrawMagnifier(DrawingContext context)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return;
        }

        var scaleX = _bitmap.PixelWidth / ActualWidth;
        var scaleY = _bitmap.PixelHeight / ActualHeight;
        var centerX = Math.Clamp((int)Math.Round(_pointer.X * scaleX), 8, Math.Max(8, _bitmap.PixelWidth - 9));
        var centerY = Math.Clamp((int)Math.Round(_pointer.Y * scaleY), 8, Math.Max(8, _bitmap.PixelHeight - 9));
        if (_bitmap.PixelWidth < 17 || _bitmap.PixelHeight < 17)
        {
            return;
        }

        var crop = new CroppedBitmap(_bitmap, new Int32Rect(centerX - 8, centerY - 8, 17, 17));
        var width = 172d;
        var imageHeight = 156d;
        var panelHeight = 218d;
        var x = _pointer.X + 24;
        var y = _pointer.Y + 24;
        if (x + width > ActualWidth)
        {
            x = _pointer.X - width - 24;
        }

        if (y + panelHeight > ActualHeight)
        {
            y = _pointer.Y - panelHeight - 24;
        }

        var panel = new Rect(x, y, width, panelHeight);
        context.DrawRectangle(new SolidColorBrush(Color.FromArgb(238, 17, 24, 39)), new Pen(Brushes.White, 1), panel);
        context.PushGuidelineSet(new GuidelineSet());
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);
        context.DrawImage(crop, new Rect(x + 8, y + 8, imageHeight - 16, imageHeight - 16));
        context.Pop();
        var imageCenterX = x + 8 + (imageHeight - 16) / 2;
        var imageCenterY = y + 8 + (imageHeight - 16) / 2;
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1), new Point(imageCenterX, y + 8), new Point(imageCenterX, y + imageHeight - 8));
        context.DrawLine(new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1), new Point(x + 8, imageCenterY), new Point(x + imageHeight - 8, imageCenterY));

        var color = CurrentPixelColor();
        var displayedColor = _showRgbColor
            ? $"RGB  {color.R}, {color.G}, {color.B}"
            : $"HEX  #{color.R:X2}{color.G:X2}{color.B:X2}";
        var info = $"({centerX}, {centerY})\n{displayedColor}\nShift 切换 · C 复制";
        var formatted = new FormattedText(
            info,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Consolas"),
            11.5,
            Brushes.White,
            VisualTreeHelper.GetDpi(this).PixelsPerDip);
        context.DrawText(formatted, new Point(x + 12, y + imageHeight + 7));

        if (_copiedColorMessage is not null)
        {
            var copied = new FormattedText(
                _copiedColorMessage,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI Semibold"),
                12,
                Brushes.White,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
            var copiedBounds = new Rect(x, y - 31, Math.Max(width, copied.Width + 20), 27);
            context.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(235, 2, 132, 199)), null, copiedBounds, 5, 5);
            context.DrawText(copied, new Point(copiedBounds.X + 10, copiedBounds.Y + 5));
        }
    }

    private Color CurrentPixelColor()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
        {
            return Colors.Transparent;
        }

        var x = Math.Clamp((int)Math.Round(_pointer.X * _bitmap.PixelWidth / ActualWidth), 0, _bitmap.PixelWidth - 1);
        var y = Math.Clamp((int)Math.Round(_pointer.Y * _bitmap.PixelHeight / ActualHeight), 0, _bitmap.PixelHeight - 1);
        var pixel = new byte[4];
        _bitmap.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    private Rect ApplyDrag(Point point)
    {
        if (_dragOperation == DragOperation.New)
        {
            return Normalize(_anchor, point);
        }

        var left = _startSelection.Left;
        var top = _startSelection.Top;
        var right = _startSelection.Right;
        var bottom = _startSelection.Bottom;
        var dx = point.X - _anchor.X;
        var dy = point.Y - _anchor.Y;

        switch (_dragOperation)
        {
            case DragOperation.Move:
                var movedX = Math.Clamp(left + dx, 0, Math.Max(0, ActualWidth - _startSelection.Width));
                var movedY = Math.Clamp(top + dy, 0, Math.Max(0, ActualHeight - _startSelection.Height));
                return new Rect(movedX, movedY, _startSelection.Width, _startSelection.Height);
            case DragOperation.Left:
                left += dx;
                break;
            case DragOperation.Right:
                right += dx;
                break;
            case DragOperation.Top:
                top += dy;
                break;
            case DragOperation.Bottom:
                bottom += dy;
                break;
            case DragOperation.TopLeft:
                left += dx;
                top += dy;
                break;
            case DragOperation.TopRight:
                right += dx;
                top += dy;
                break;
            case DragOperation.BottomLeft:
                left += dx;
                bottom += dy;
                break;
            case DragOperation.BottomRight:
                right += dx;
                bottom += dy;
                break;
        }

        left = Math.Clamp(left, 0, ActualWidth);
        right = Math.Clamp(right, 0, ActualWidth);
        top = Math.Clamp(top, 0, ActualHeight);
        bottom = Math.Clamp(bottom, 0, ActualHeight);
        return Normalize(new Point(left, top), new Point(right, bottom));
    }

    private DragOperation HitTestOperation(Point point)
    {
        if (Selection is not { } selection)
        {
            return DragOperation.None;
        }

        var points = HandlePoints(selection).ToArray();
        var operations = new[]
        {
            DragOperation.TopLeft, DragOperation.Top, DragOperation.TopRight, DragOperation.Right,
            DragOperation.BottomRight, DragOperation.Bottom, DragOperation.BottomLeft, DragOperation.Left
        };

        for (var index = 0; index < points.Length; index++)
        {
            if ((points[index] - point).Length <= 10)
            {
                return operations[index];
            }
        }

        return selection.Contains(point) ? DragOperation.Move : DragOperation.None;
    }

    private static IAnnotation CreateAnnotation(AnnotationTool tool, Point start, Color color, double thickness) => tool switch
    {
        AnnotationTool.Pen => new PenAnnotation(start, CreateStroke(color, thickness)),
        AnnotationTool.Rectangle => new RectangleAnnotation(start, CreateStroke(color, thickness)),
        AnnotationTool.Arrow => new ArrowAnnotation(start, CreateStroke(color, thickness)),
        _ => throw new InvalidOperationException("当前工具不能创建标注。")
    };

    private static Cursor CursorFor(DragOperation operation) => operation switch
    {
        DragOperation.Move => Cursors.SizeAll,
        DragOperation.Left or DragOperation.Right => Cursors.SizeWE,
        DragOperation.Top or DragOperation.Bottom => Cursors.SizeNS,
        DragOperation.TopLeft or DragOperation.BottomRight => Cursors.SizeNWSE,
        DragOperation.TopRight or DragOperation.BottomLeft => Cursors.SizeNESW,
        _ => Cursors.Cross
    };

    private static IEnumerable<Point> HandlePoints(Rect rectangle)
    {
        yield return rectangle.TopLeft;
        yield return new Point(rectangle.Left + rectangle.Width / 2, rectangle.Top);
        yield return rectangle.TopRight;
        yield return new Point(rectangle.Right, rectangle.Top + rectangle.Height / 2);
        yield return rectangle.BottomRight;
        yield return new Point(rectangle.Left + rectangle.Width / 2, rectangle.Bottom);
        yield return rectangle.BottomLeft;
        yield return new Point(rectangle.Left, rectangle.Top + rectangle.Height / 2);
    }

    private static Rect Normalize(Point first, Point second) => new(
        new Point(Math.Min(first.X, second.X), Math.Min(first.Y, second.Y)),
        new Point(Math.Max(first.X, second.X), Math.Max(first.Y, second.Y)));

    private Point Constrain(Point point) => new(
        Math.Clamp(point.X, 0, Math.Max(0, ActualWidth)),
        Math.Clamp(point.Y, 0, Math.Max(0, ActualHeight)));

    private void RaiseSelectionChanged()
    {
        if (Selection is { } selection)
        {
            SelectionChanged?.Invoke(this, selection);
        }
    }

    private enum DragOperation
    {
        None,
        New,
        Move,
        Left,
        Top,
        Right,
        Bottom,
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight
    }

    private interface IAnnotation
    {
        bool IsMeaningful { get; }
        void Update(Point point);
        void Draw(DrawingContext context);
    }

    private sealed class PenAnnotation : IAnnotation
    {
        private readonly List<Point> _points = [];
        private readonly Pen _stroke;

        internal PenAnnotation(Point start, Pen stroke)
        {
            _points.Add(start);
            _stroke = stroke;
        }
        public bool IsMeaningful => _points.Count > 1 && (_points[^1] - _points[0]).Length > 2;

        public void Update(Point point)
        {
            if ((_points[^1] - point).Length >= 1.5)
            {
                _points.Add(point);
            }
        }

        public void Draw(DrawingContext context)
        {
            for (var index = 1; index < _points.Count; index++)
            {
                context.DrawLine(_stroke, _points[index - 1], _points[index]);
            }
        }
    }

    private abstract class TwoPointAnnotation : IAnnotation
    {
        protected TwoPointAnnotation(Point start, Pen stroke)
        {
            Start = start;
            End = start;
            Stroke = stroke;
        }

        protected Pen Stroke { get; }
        protected Point Start { get; }
        protected Point End { get; private set; }
        public bool IsMeaningful => (End - Start).Length > 3;
        public void Update(Point point) => End = point;
        public abstract void Draw(DrawingContext context);
    }

    private sealed class RectangleAnnotation : TwoPointAnnotation
    {
        internal RectangleAnnotation(Point start, Pen stroke) : base(start, stroke) { }

        public override void Draw(DrawingContext context) =>
            context.DrawRectangle(null, Stroke, Normalize(Start, End));
    }

    private sealed class ArrowAnnotation : TwoPointAnnotation
    {
        internal ArrowAnnotation(Point start, Pen stroke) : base(start, stroke) { }

        public override void Draw(DrawingContext context)
        {
            context.DrawLine(Stroke, Start, End);
            var vector = Start - End;
            if (vector.Length < 1)
            {
                return;
            }

            vector.Normalize();
            var normal = new Vector(-vector.Y, vector.X);
            const double headLength = 13;
            const double headWidth = 6;
            context.DrawLine(Stroke, End, End + vector * headLength + normal * headWidth);
            context.DrawLine(Stroke, End, End + vector * headLength - normal * headWidth);
        }
    }

    private sealed class TextAnnotation : IAnnotation
    {
        private readonly Point _position;
        private string _text;
        private readonly double _fontSize;
        private readonly Color _color;
        private readonly bool _bold;
        private bool _editing;

        internal TextAnnotation(Point position, string text, double fontSize, Color color, bool bold, bool editing = false)
        {
            _position = position;
            _text = text;
            _fontSize = fontSize;
            _color = color;
            _bold = bold;
            _editing = editing;
        }

        public bool IsMeaningful => !string.IsNullOrWhiteSpace(_text);
        public void Update(Point point) { }
        internal void SetText(string text) => _text = text;
        internal void FinishEditing() => _editing = false;

        public void Draw(DrawingContext context)
        {
            var brush = new SolidColorBrush(_color);
            var typeface = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal,
                _bold ? FontWeights.Bold : FontWeights.Normal, FontStretches.Normal);
            var formatted = new FormattedText(
                string.IsNullOrEmpty(_text) ? " " : _text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                typeface,
                _fontSize,
                brush,
                1)
            {
                MaxTextWidth = 420
            };
            context.DrawText(formatted, _position);

            if (_editing)
            {
                var lines = _text.Replace("\r", string.Empty).Split('\n');
                var lastLine = lines.Length == 0 ? string.Empty : lines[^1];
                var caretMeasure = new FormattedText(
                    lastLine,
                    CultureInfo.CurrentCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    _fontSize,
                    brush,
                    1);
                var caretX = _position.X + caretMeasure.WidthIncludingTrailingWhitespace + 1;
                var caretY = _position.Y + Math.Max(0, lines.Length - 1) * formatted.LineHeight;
                var caretPen = new Pen(new SolidColorBrush(Color.FromRgb(56, 189, 248)), 1.6);
                context.DrawLine(caretPen, new Point(caretX, caretY), new Point(caretX, caretY + _fontSize * 1.25));

                if (string.IsNullOrEmpty(_text))
                {
                    var badge = new FormattedText(
                        "T",
                        CultureInfo.InvariantCulture,
                        FlowDirection.LeftToRight,
                        new Typeface("Segoe UI Bold"),
                        10,
                        Brushes.White,
                        1);
                    var badgeBounds = new Rect(_position.X - 16, _position.Y - 15, 14, 14);
                    context.DrawRoundedRectangle(new SolidColorBrush(Color.FromRgb(2, 132, 199)), null, badgeBounds, 3, 3);
                    context.DrawText(badge, new Point(badgeBounds.X + 3.5, badgeBounds.Y + 1));
                }
            }
        }
    }

    private static Pen CreateStroke(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }
}

internal enum AnnotationTool
{
    Select,
    Pen,
    Rectangle,
    Arrow,
    Text
}
