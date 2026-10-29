using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Ahnen.Core.Models;
using Ahnen.Desktop.Localization;

namespace Ahnen.Desktop.Visualization;

/// <summary>One native drawing surface, rather than a WPF control for every person or family link.</summary>
public sealed class TreeCanvas : FrameworkElement
{
    public static readonly DependencyProperty SceneProperty = DependencyProperty.Register(nameof(Scene), typeof(TreeScene), typeof(TreeCanvas),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((TreeCanvas)d).Fit()));
    public static readonly DependencyProperty SelectedIdProperty = RenderProperty(nameof(SelectedId), typeof(string), null);
    public static readonly DependencyProperty FocusIdProperty = RenderProperty(nameof(FocusId), typeof(string), null);
    public static readonly DependencyProperty RootIdProperty = RenderProperty(nameof(RootId), typeof(string), null);
    public static readonly DependencyProperty ColorsProperty = RenderProperty(nameof(Colors), typeof(TreeColors), TreeColors.Sex);
    public static readonly DependencyProperty ShowIdsProperty = RenderProperty(nameof(ShowIds), typeof(bool), false);
    public static readonly DependencyProperty ShowSexProperty = RenderProperty(nameof(ShowSex), typeof(bool), true);
    public static readonly DependencyProperty DataDirectoryProperty = RenderProperty(nameof(DataDirectory), typeof(string), "");
    public static readonly DependencyProperty PhotosProperty = DependencyProperty.Register(nameof(Photos), typeof(TreePhotoCache), typeof(TreeCanvas),
        new PropertyMetadata(null, (d, e) => ((TreeCanvas)d).ChangePhotoCache(e.OldValue as TreePhotoCache, e.NewValue as TreePhotoCache)));

    public TreeScene? Scene { get => (TreeScene?)GetValue(SceneProperty); set => SetValue(SceneProperty, value); }
    public string? SelectedId { get => (string?)GetValue(SelectedIdProperty); set => SetValue(SelectedIdProperty, value); }
    public string? FocusId { get => (string?)GetValue(FocusIdProperty); set => SetValue(FocusIdProperty, value); }
    public string? RootId { get => (string?)GetValue(RootIdProperty); set => SetValue(RootIdProperty, value); }
    public TreeColors Colors { get => (TreeColors)GetValue(ColorsProperty); set => SetValue(ColorsProperty, value); }
    public bool ShowIds { get => (bool)GetValue(ShowIdsProperty); set => SetValue(ShowIdsProperty, value); }
    public bool ShowSex { get => (bool)GetValue(ShowSexProperty); set => SetValue(ShowSexProperty, value); }
    public string DataDirectory { get => (string)GetValue(DataDirectoryProperty); set => SetValue(DataDirectoryProperty, value); }
    public TreePhotoCache? Photos { get => (TreePhotoCache?)GetValue(PhotosProperty); set => SetValue(PhotosProperty, value); }

    private static readonly Brush Ink = Brush("#1D2733");
    private static readonly Brush Muted = Brush("#566379");
    private static readonly Brush Background = Brush("#F4F6FA");
    private static readonly Brush Male = Brush("#DCEBFF");
    private static readonly Brush Female = Brush("#FCE0EB");
    private static readonly Brush Other = Brush("#E9E1FF");
    private static readonly Brush Above = Brush("#FFE6C9");
    private static readonly Brush Below = Brush("#D5F1F4");
    private static readonly Pen Outline = Pen("#8191A6", 1.2);
    private static readonly Pen Selected = Pen("#1D2733", 3);
    private static readonly Pen FocusOutline = Pen("#2563EB", 3);
    private static readonly Pen RootOutline = Pen("#C2410C", 2);
    private static readonly Pen[] Links = [Pen("#8191A6", 1.5), Pen("#8191A6", 1.5, DashStyles.Dash),
        Pen("#8191A6", 1.5, DashStyles.Dot), Pen("#7C3AED", 1.5, DashStyles.Dash)];
    private static readonly Typeface Typeface = new("Segoe UI");
    private double _scale = 1;
    private Vector _offset;
    private Point _dragStart;
    private Vector _dragOffset;
    private bool _dragging;
    private bool _panningMinimap;
    private Rect _minimap;
    private double _mapScale;
    private Point _mapOffset;

    public double ZoomLevel => _scale;
    public event Action<string, bool>? PersonActivated;

    public TreeCanvas()
    {
        Focusable = true;
        ClipToBounds = true;
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => Fit();
        Loaded += (_, _) => { if (Photos is { } photos) { photos.ImageLoaded -= PhotoLoaded; photos.ImageLoaded += PhotoLoaded; } };
        Unloaded += (_, _) => { if (Photos is { } photos) photos.ImageLoaded -= PhotoLoaded; };
    }

    private static DependencyProperty RenderProperty(string name, Type type, object? value) =>
        DependencyProperty.Register(name, type, typeof(TreeCanvas), new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public void Fit()
    {
        if (Scene is not { } scene || ActualWidth <= 0 || ActualHeight <= 0) return;
        _scale = Math.Clamp(Math.Min((ActualWidth - 40) / scene.Bounds.Width, (ActualHeight - 40) / scene.Bounds.Height), 0.00001, 1.5);
        _offset = new Vector((ActualWidth - scene.Bounds.Width * _scale) / 2, (ActualHeight - scene.Bounds.Height * _scale) / 2);
        InvalidateVisual();
    }

    public void Zoom(double factor) => ZoomAt(factor, new Point(ActualWidth / 2, ActualHeight / 2));

    private void ZoomAt(double factor, Point anchor)
    {
        var next = Math.Clamp(_scale * factor, 0.00001, 3);
        _offset = (Vector)anchor - ((Vector)anchor - _offset) * (next / _scale);
        _scale = next;
        InvalidateVisual();
    }

    public void CenterOn(string personId)
    {
        if (Scene?.Cards.FirstOrDefault(c => c.Person.Id == personId) is not { } card) return;
        _scale = Math.Max(_scale, 0.85);
        _offset = new Vector(ActualWidth / 2 - card.Bounds.Center.X * _scale, ActualHeight / 2 - card.Bounds.Center.Y * _scale);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        dc.DrawRectangle(Background, null, new Rect(RenderSize));
        if (Scene is not { } scene) return;
        var viewport = new ChartBounds(-_offset.X / _scale, -_offset.Y / _scale, ActualWidth / _scale, ActualHeight / _scale);
        dc.PushTransform(new MatrixTransform(_scale, 0, 0, _scale, _offset.X, _offset.Y));
        foreach (var link in scene.Links)
        {
            var minX = link.Points.Min(p => p.X);
            var minY = link.Points.Min(p => p.Y);
            if (!new ChartBounds(minX, minY, link.Points.Max(p => p.X) - minX, link.Points.Max(p => p.Y) - minY).Intersects(viewport)) continue;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(Point(link.Points[0]), false, false);
                foreach (var p in link.Points.Skip(1)) context.LineTo(Point(p), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(null, Links[(int)link.Style], geometry);
        }
        if (_scale >= 0.25)
            foreach (var junction in scene.Junctions)
                if (viewport.Contains(junction.Position))
                    dc.DrawEllipse(Muted, null, Point(junction.Position), 3, 3);
        foreach (var card in scene.InViewport(viewport)) DrawCard(dc, card);
        dc.Pop();
        DrawMinimap(dc, scene, viewport);
        if (IsKeyboardFocused) dc.DrawRectangle(null, FocusOutline, new Rect(2, 2, Math.Max(0, ActualWidth - 4), Math.Max(0, ActualHeight - 4)));
    }

    private void DrawCard(DrawingContext dc, TreeCard card)
    {
        var bounds = ToRect(card.Bounds);
        var fill = Colors switch
        {
            TreeColors.Sex => card.Person.Sex switch { Sex.M => Male, Sex.F => Female, Sex.X => Other, _ => Brushes.White },
            TreeColors.Generation => card.Generation > 0 ? Above : card.Generation < 0 ? Below : Other,
            _ => Brushes.White
        };
        dc.DrawRoundedRectangle(fill, Outline, bounds, 9, 9);
        if (card.Person.Id == RootId)
            dc.DrawRoundedRectangle(null, RootOutline, new Rect(bounds.X + 4, bounds.Y + 4, bounds.Width - 8, bounds.Height - 8), 6, 6);
        if (card.Person.Id == FocusId) dc.DrawRoundedRectangle(null, FocusOutline, bounds, 9, 9);
        if (card.Person.Id == SelectedId) dc.DrawRoundedRectangle(null, Selected, bounds, 9, 9);
        if (_scale < 0.18) return;
        var photo = _scale >= 0.5 ? Photos?.Get(DataDirectory, card.Person.PhotoFile) : null;
        var textX = bounds.X + 12;
        if (photo is not null)
        {
            dc.DrawImage(photo, new Rect(bounds.X + 10, bounds.Y + 26, 48, 48));
            textX += 54;
        }
        DrawText(dc, card.Person.DisplayName, new Point(textX, bounds.Y + 14), 18, bounds.Right - textX - 10, Ink);
        if (_scale < 0.35) return;
        DrawText(dc, card.Person.LifeYears, new Point(textX, bounds.Y + 43), 15, bounds.Right - textX - 10, Muted);
        var sex = ShowSex ? card.Person.Sex switch { Sex.M => "\u2642", Sex.F => "\u2640", Sex.X => "\u26A5", _ => "" } : "";
        var generation = card.Generation > 0 ? $"G+{card.Generation}" : $"G{card.Generation}";
        var footer = string.Join("  ", new[] { generation, ShowIds ? card.Person.Id : "", sex, card.Collapsed ? "+" : "" }.Where(s => s.Length > 0));
        DrawText(dc, footer, new Point(textX, bounds.Y + 72), 13, bounds.Right - textX - 10, Muted);
    }

    private void DrawText(DrawingContext dc, string text, Point position, double size, double width, Brush brush)
    {
        if (string.IsNullOrEmpty(text) || width <= 0) return;
        var formatted = new FormattedText(text.Replace('\n', ' ').Replace('\r', ' '), Localizer.Instance.Culture,
            FlowDirection.LeftToRight, Typeface, size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
        dc.DrawText(formatted, position);
    }

    private void DrawMinimap(DrawingContext dc, TreeScene scene, ChartBounds viewport)
    {
        if (ActualWidth < 240 || ActualHeight < 180 || scene.Cards.Count == 0) { _minimap = Rect.Empty; return; }
        _minimap = new Rect(12, ActualHeight - 136, 200, 124);
        dc.DrawRoundedRectangle(Brushes.White, Outline, _minimap, 4, 4);
        _mapScale = Math.Min(188 / scene.Bounds.Width, 112 / scene.Bounds.Height);
        _mapOffset = new Point(_minimap.X + 6 + (188 - scene.Bounds.Width * _mapScale) / 2,
            _minimap.Y + 6 + (112 - scene.Bounds.Height * _mapScale) / 2);
        dc.PushClip(new RectangleGeometry(_minimap));
        foreach (var card in scene.Cards)
            dc.DrawRectangle(card.Person.Id == SelectedId ? FocusOutline.Brush : Muted, null,
                new Rect(_mapOffset.X + card.Bounds.X * _mapScale, _mapOffset.Y + card.Bounds.Y * _mapScale,
                    Math.Max(1, card.Bounds.Width * _mapScale), Math.Max(1, card.Bounds.Height * _mapScale)));
        dc.DrawRectangle(null, FocusOutline, new Rect(_mapOffset.X + viewport.X * _mapScale, _mapOffset.Y + viewport.Y * _mapScale,
            viewport.Width * _mapScale, viewport.Height * _mapScale));
        dc.Pop();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        ZoomAt(Math.Pow(1.2, e.Delta / 120.0), e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        _dragStart = e.GetPosition(this);
        _panningMinimap = _minimap.Contains(_dragStart);
        _dragging = !_panningMinimap;
        _dragOffset = _offset;
        if (_panningMinimap) PanFromMinimap(_dragStart);
        CaptureMouse();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (_dragging && e.LeftButton == MouseButtonState.Pressed)
        {
            _offset = _dragOffset + point - _dragStart;
            InvalidateVisual();
        }
        else if (_panningMinimap && e.LeftButton == MouseButtonState.Pressed)
            PanFromMinimap(point);
        else if (Scene?.HitTest(new((point.X - _offset.X) / _scale, (point.Y - _offset.Y) / _scale)) is { } card)
            ToolTip = card.Person.DisplayName + " " + card.Person.LifeYears + " (" + card.Person.Id + ")";
        else ToolTip = null;
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        var point = e.GetPosition(this);
        if (_dragging && (point - _dragStart).Length < 5
            && Scene?.HitTest(new((point.X - _offset.X) / _scale, (point.Y - _offset.Y) / _scale)) is { } card)
            PersonActivated?.Invoke(card.Person.Id, (Keyboard.Modifiers & ModifierKeys.Shift) == 0);
        _dragging = false;
        _panningMinimap = false;
        ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _dragging = false;
        _panningMinimap = false;
        base.OnLostMouseCapture(e);
    }

    private void PanFromMinimap(Point point)
    {
        if (_mapScale <= 0) return;
        _offset = new Vector(ActualWidth / 2 - (point.X - _mapOffset.X) / _mapScale * _scale,
            ActualHeight / 2 - (point.Y - _mapOffset.Y) / _mapScale * _scale);
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        switch (e.Key)
        {
            case Key.Add: case Key.OemPlus: Zoom(1.25); break;
            case Key.Subtract: case Key.OemMinus: Zoom(0.8); break;
            case Key.Home: Fit(); break;
            case Key.Left: _offset.X += 60; InvalidateVisual(); break;
            case Key.Right: _offset.X -= 60; InvalidateVisual(); break;
            case Key.Up: _offset.Y += 60; InvalidateVisual(); break;
            case Key.Down: _offset.Y -= 60; InvalidateVisual(); break;
            default: return;
        }
        e.Handled = true;
    }

    private void ChangePhotoCache(TreePhotoCache? oldCache, TreePhotoCache? newCache)
    {
        if (oldCache is not null) oldCache.ImageLoaded -= PhotoLoaded;
        if (newCache is not null && IsLoaded) newCache.ImageLoaded += PhotoLoaded;
        InvalidateVisual();
    }
    private void PhotoLoaded() => InvalidateVisual();
    private static Point Point(ChartPoint p) => new(p.X, p.Y);
    private static Rect ToRect(ChartBounds b) => new(b.X, b.Y, b.Width, b.Height);
    private static SolidColorBrush Brush(string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        return brush;
    }
    private static Pen Pen(string color, double width, DashStyle? dash = null)
    {
        var pen = new Pen(Brush(color), width) { DashStyle = dash ?? DashStyles.Solid };
        pen.Freeze();
        return pen;
    }
}
