using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClipboardPal.Core.Abstractions;

namespace ClipboardPal;

/// <summary>
/// Picks a region of a stored image: drag to select, drag the handles to adjust, and the view
/// follows the selection so small areas can be framed precisely. Zoom is also under manual
/// control. Everything is kept in image pixels, so the crop is taken at full resolution no
/// matter how the preview happens to be scaled.
/// </summary>
public partial class RegionPickerWindow : Window
{
    /// <summary>Which part of the selection a drag is grabbing.</summary>
    private enum Grip
    {
        None, NewSelection, Move,
        TopLeft, Top, TopRight, Right, BottomRight, Bottom, BottomLeft, Left
    }

    private const double HandleSize = 9;
    private const double GrabTolerance = 7;
    private const double MinZoom = 0.05;
    private const double MaxZoom = 12;

    private readonly Bitmap _bitmap;
    private readonly Rectangle[] _handles = new Rectangle[8];

    private double _zoom = 1;
    private Point _pan;
    private Rect _selection;

    private Grip _grip = Grip.None;
    private Point _dragOrigin;
    private Rect _selectionAtDragStart;
    private bool _panning;
    private Point _panOrigin;

    /// <summary>Chosen region in source pixels, or null when the window was dismissed.</summary>
    public PixelRect? Result { get; private set; }

    public RegionPickerWindow(Bitmap bitmap, ILocalizationService l10n)
    {
        InitializeComponent();
        _bitmap = bitmap;
        Preview.Source = bitmap;

        HintText.Text = l10n["region.hint"];
        WholeButton.Content = l10n["region.whole"];
        CancelButton.Content = l10n["region.cancel"];
        AcceptButton.Content = l10n["region.accept"];
        FitButton.Content = l10n["region.fit"];

        var work = Screens.Primary?.WorkingArea;
        Width = Math.Min(1180, (work?.Width ?? 1280) * 0.82);
        Height = Math.Min(820, (work?.Height ?? 800) * 0.82);

        for (var i = 0; i < _handles.Length; i++)
        {
            var handle = new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(0x4C, 0x8D, 0xFF)),
                StrokeThickness = 1.5,
                IsVisible = false
            };
            _handles[i] = handle;
            Surface.Children.Add(handle);
        }

        // Laid out once the canvas knows its size. Re-fitting only while nothing is selected
        // keeps a resize from throwing away a zoom the user set up around their selection.
        Surface.PropertyChanged += (_, e) =>
        {
            if (e.Property != BoundsProperty || Surface.Bounds is { Width: < 1 } or { Height: < 1 })
                return;
            if (_selection is { Width: > 0, Height: > 0 })
                ApplyLayout();
            else
                FitWholeImage();
        };
    }

    // ---- coordinate mapping -------------------------------------------------

    private Point ToImage(Point screen) =>
        new((screen.X - _pan.X) / _zoom, (screen.Y - _pan.Y) / _zoom);

    private Point ToScreen(Point image) =>
        new(image.X * _zoom + _pan.X, image.Y * _zoom + _pan.Y);

    private Size ViewportSize => new(
        Math.Max(1, Surface.Bounds.Width),
        Math.Max(1, Surface.Bounds.Height));

    // ---- zoom and pan -------------------------------------------------------

    private void FitWholeImage()
    {
        var view = ViewportSize;
        _zoom = Math.Clamp(
            Math.Min(view.Width / _bitmap.PixelSize.Width, view.Height / _bitmap.PixelSize.Height),
            MinZoom, MaxZoom);
        CenterOn(new Point(_bitmap.PixelSize.Width / 2.0, _bitmap.PixelSize.Height / 2.0));
    }

    /// <summary>
    /// Brings the selection up close, leaving a margin so its handles stay reachable. This is what
    /// makes the view follow the selection as it is resized.
    /// </summary>
    private void FitSelection()
    {
        if (_selection is not { Width: > 0, Height: > 0 })
            return;

        var view = ViewportSize;
        var target = Math.Min(
            view.Width * 0.8 / _selection.Width,
            view.Height * 0.8 / _selection.Height);

        _zoom = Math.Clamp(target, MinZoom, MaxZoom);
        CenterOn(_selection.Center);
    }

    private void CenterOn(Point imagePoint)
    {
        var view = ViewportSize;
        _pan = new Point(
            view.Width / 2 - imagePoint.X * _zoom,
            view.Height / 2 - imagePoint.Y * _zoom);
        ClampPan();
        ApplyLayout();
    }

    private void SetZoom(double zoom, Point anchorScreen)
    {
        var before = ToImage(anchorScreen);
        _zoom = Math.Clamp(zoom, MinZoom, MaxZoom);
        var after = ToScreen(before);
        _pan = new Point(_pan.X + (anchorScreen.X - after.X), _pan.Y + (anchorScreen.Y - after.Y));
        ClampPan();
        ApplyLayout();
    }

    /// <summary>Keeps the picture from being dragged off the viewport entirely.</summary>
    private void ClampPan()
    {
        var view = ViewportSize;
        var width = _bitmap.PixelSize.Width * _zoom;
        var height = _bitmap.PixelSize.Height * _zoom;

        var x = width <= view.Width
            ? (view.Width - width) / 2
            : Math.Clamp(_pan.X, view.Width - width, 0);
        var y = height <= view.Height
            ? (view.Height - height) / 2
            : Math.Clamp(_pan.Y, view.Height - height, 0);

        _pan = new Point(x, y);
    }

    // ---- rendering ----------------------------------------------------------

    private void ApplyLayout()
    {
        Preview.Width = _bitmap.PixelSize.Width * _zoom;
        Preview.Height = _bitmap.PixelSize.Height * _zoom;
        Canvas.SetLeft(Preview, _pan.X);
        Canvas.SetTop(Preview, _pan.Y);

        ZoomText.Text = $"{Math.Round(_zoom * 100)}%";

        var visible = _selection is { Width: > 0, Height: > 0 };
        Marquee.IsVisible = visible;
        AcceptButton.IsEnabled = visible && _selection is { Width: >= 4, Height: >= 4 };

        if (!visible)
        {
            foreach (var handle in _handles)
                handle.IsVisible = false;
            SelectionText.Text = string.Empty;
            return;
        }

        var topLeft = ToScreen(_selection.TopLeft);
        Canvas.SetLeft(Marquee, topLeft.X);
        Canvas.SetTop(Marquee, topLeft.Y);
        Marquee.Width = _selection.Width * _zoom;
        Marquee.Height = _selection.Height * _zoom;

        var right = topLeft.X + Marquee.Width;
        var bottom = topLeft.Y + Marquee.Height;
        var midX = topLeft.X + Marquee.Width / 2;
        var midY = topLeft.Y + Marquee.Height / 2;
        var spots = new[]
        {
            new Point(topLeft.X, topLeft.Y), new Point(midX, topLeft.Y), new Point(right, topLeft.Y),
            new Point(right, midY), new Point(right, bottom), new Point(midX, bottom),
            new Point(topLeft.X, bottom), new Point(topLeft.X, midY)
        };

        for (var i = 0; i < _handles.Length; i++)
        {
            _handles[i].IsVisible = true;
            Canvas.SetLeft(_handles[i], spots[i].X - HandleSize / 2);
            Canvas.SetTop(_handles[i], spots[i].Y - HandleSize / 2);
        }

        var rect = ToPixelRect(_selection);
        SelectionText.Text = $"{rect.Width} × {rect.Height}";
    }

    private PixelRect ToPixelRect(Rect selection)
    {
        var x = (int)Math.Round(Math.Clamp(selection.X, 0, _bitmap.PixelSize.Width));
        var y = (int)Math.Round(Math.Clamp(selection.Y, 0, _bitmap.PixelSize.Height));
        var width = (int)Math.Round(Math.Clamp(selection.Width, 0, _bitmap.PixelSize.Width - x));
        var height = (int)Math.Round(Math.Clamp(selection.Height, 0, _bitmap.PixelSize.Height - y));
        return new PixelRect(x, y, width, height);
    }

    // ---- hit testing --------------------------------------------------------

    private Grip HitTest(Point screen)
    {
        if (_selection is not { Width: > 0, Height: > 0 })
            return Grip.NewSelection;

        var topLeft = ToScreen(_selection.TopLeft);
        var bottomRight = ToScreen(_selection.BottomRight);
        var nearLeft = Math.Abs(screen.X - topLeft.X) <= GrabTolerance;
        var nearRight = Math.Abs(screen.X - bottomRight.X) <= GrabTolerance;
        var nearTop = Math.Abs(screen.Y - topLeft.Y) <= GrabTolerance;
        var nearBottom = Math.Abs(screen.Y - bottomRight.Y) <= GrabTolerance;
        var insideX = screen.X >= topLeft.X - GrabTolerance && screen.X <= bottomRight.X + GrabTolerance;
        var insideY = screen.Y >= topLeft.Y - GrabTolerance && screen.Y <= bottomRight.Y + GrabTolerance;

        if (nearLeft && nearTop) return Grip.TopLeft;
        if (nearRight && nearTop) return Grip.TopRight;
        if (nearRight && nearBottom) return Grip.BottomRight;
        if (nearLeft && nearBottom) return Grip.BottomLeft;
        if (nearTop && insideX) return Grip.Top;
        if (nearBottom && insideX) return Grip.Bottom;
        if (nearLeft && insideY) return Grip.Left;
        if (nearRight && insideY) return Grip.Right;

        return new Rect(topLeft, bottomRight).Contains(screen) ? Grip.Move : Grip.NewSelection;
    }

    private static Cursor CursorFor(Grip grip) => new(grip switch
    {
        Grip.Move => StandardCursorType.SizeAll,
        Grip.Top or Grip.Bottom => StandardCursorType.SizeNorthSouth,
        Grip.Left or Grip.Right => StandardCursorType.SizeWestEast,
        Grip.TopLeft or Grip.BottomRight => StandardCursorType.TopLeftCorner,
        Grip.TopRight or Grip.BottomLeft => StandardCursorType.TopRightCorner,
        _ => StandardCursorType.Cross
    });

    // ---- pointer input ------------------------------------------------------

    private void Surface_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        var position = point.Position;

        // Right button (or middle) pans, which is how you move around when zoomed in.
        if (point.Properties.IsRightButtonPressed || point.Properties.IsMiddleButtonPressed)
        {
            _panning = true;
            _panOrigin = position;
            e.Pointer.Capture(Surface);
            return;
        }

        _grip = HitTest(position);
        _dragOrigin = position;
        _selectionAtDragStart = _selection;

        if (_grip == Grip.NewSelection)
        {
            var start = ClampToImage(ToImage(position));
            _selection = new Rect(start, start);
            ApplyLayout();
        }

        e.Pointer.Capture(Surface);
    }

    private void Surface_PointerMoved(object? sender, PointerEventArgs e)
    {
        var position = e.GetPosition(Surface);

        if (_panning)
        {
            _pan = new Point(_pan.X + (position.X - _panOrigin.X), _pan.Y + (position.Y - _panOrigin.Y));
            _panOrigin = position;
            ClampPan();
            ApplyLayout();
            return;
        }

        if (_grip == Grip.None)
        {
            Surface.Cursor = CursorFor(HitTest(position));
            return;
        }

        var current = ToImage(position);
        var origin = ToImage(_dragOrigin);

        if (_grip == Grip.NewSelection)
        {
            _selection = Normalize(origin, current);
        }
        else if (_grip == Grip.Move)
        {
            var dx = current.X - origin.X;
            var dy = current.Y - origin.Y;
            var x = Math.Clamp(_selectionAtDragStart.X + dx, 0, _bitmap.PixelSize.Width - _selectionAtDragStart.Width);
            var y = Math.Clamp(_selectionAtDragStart.Y + dy, 0, _bitmap.PixelSize.Height - _selectionAtDragStart.Height);
            _selection = new Rect(x, y, _selectionAtDragStart.Width, _selectionAtDragStart.Height);
        }
        else
        {
            _selection = Resize(_selectionAtDragStart, _grip, ClampToImage(current));
        }

        ApplyLayout();
    }

    private void Surface_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var wasAdjusting = _grip is not Grip.None;
        _panning = false;
        _grip = Grip.None;
        e.Pointer.Capture(null);

        // Zoom follows the selection once the drag settles, rather than jumping on every pixel.
        if (wasAdjusting && _selection is { Width: >= 4, Height: >= 4 })
            FitSelection();
    }

    private void Surface_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        SetZoom(_zoom * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), e.GetPosition(Surface));
        e.Handled = true;
    }

    private Point ClampToImage(Point image) => new(
        Math.Clamp(image.X, 0, _bitmap.PixelSize.Width),
        Math.Clamp(image.Y, 0, _bitmap.PixelSize.Height));

    private static Rect Normalize(Point a, Point b) => new(
        Math.Min(a.X, b.X), Math.Min(a.Y, b.Y),
        Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    /// <summary>Moves the edges the grabbed handle owns, letting the rectangle flip through zero.</summary>
    private static Rect Resize(Rect start, Grip grip, Point point)
    {
        var left = start.X;
        var top = start.Y;
        var right = start.Right;
        var bottom = start.Bottom;

        if (grip is Grip.TopLeft or Grip.Left or Grip.BottomLeft) left = point.X;
        if (grip is Grip.TopRight or Grip.Right or Grip.BottomRight) right = point.X;
        if (grip is Grip.TopLeft or Grip.Top or Grip.TopRight) top = point.Y;
        if (grip is Grip.BottomLeft or Grip.Bottom or Grip.BottomRight) bottom = point.Y;

        return Normalize(new Point(left, top), new Point(right, bottom));
    }

    // ---- buttons and keys ---------------------------------------------------

    private void ZoomIn_Click(object? sender, RoutedEventArgs e) =>
        SetZoom(_zoom * 1.25, new Point(ViewportSize.Width / 2, ViewportSize.Height / 2));

    private void ZoomOut_Click(object? sender, RoutedEventArgs e) =>
        SetZoom(_zoom / 1.25, new Point(ViewportSize.Width / 2, ViewportSize.Height / 2));

    private void Fit_Click(object? sender, RoutedEventArgs e) => FitWholeImage();

    private void Accept_Click(object? sender, RoutedEventArgs e)
    {
        var rect = ToPixelRect(_selection);
        if (rect.Width < 4 || rect.Height < 4)
            return;

        Result = rect;
        Close();
    }

    private void Whole_Click(object? sender, RoutedEventArgs e)
    {
        Result = new PixelRect(0, 0, _bitmap.PixelSize.Width, _bitmap.PixelSize.Height);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Window_KeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                break;
            case Key.Enter when AcceptButton.IsEnabled:
                Accept_Click(sender, e);
                break;
            case Key.OemPlus or Key.Add:
                ZoomIn_Click(sender, e);
                break;
            case Key.OemMinus or Key.Subtract:
                ZoomOut_Click(sender, e);
                break;
            case Key.D0 or Key.NumPad0:
                FitWholeImage();
                break;
            default:
                return;
        }

        e.Handled = true;
    }
}
