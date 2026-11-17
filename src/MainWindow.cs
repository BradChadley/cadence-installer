using System.Diagnostics;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Transformation;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;
using Path = Avalonia.Controls.Shapes.Path;

namespace BradleysApps;

/// <summary>The colors: Cadence's dark palette and its rose accent.</summary>
static class Ink
{
    public static IBrush Of(string hex) => new SolidColorBrush(Color.Parse(hex));
    public static readonly IBrush Canvas = Of("#0F0F11");
    public static readonly IBrush Title = Of("#F4F1F6");
    public static readonly IBrush Soft = Of("#A09CA8");
    public static readonly IBrush Meta = Of("#8B8794");
    public static readonly IBrush Accent = Of("#C38A9B");
    public static readonly IBrush Link = Of("#D9A3BB");
    public static readonly IBrush Green = Of("#86D3A3");
    public static readonly IBrush Red = Of("#EC8E8E");
    public static readonly IBrush Amber = Of("#E3C08A");
    public static readonly IBrush Rule = Of("#26262C");
    public static readonly IBrush RuleHover = Of("#3A3840");
    public static readonly IBrush RulePicked = Of("#8C6A76");
    public static readonly IBrush Ring = Of("#4B4752");
    public static readonly FontFamily Font = new("Segoe UI Variable Display, Segoe UI");
    public static readonly FontFamily TextFont = new("Segoe UI Variable Text, Segoe UI");
}

/// <summary>One app: its picture, name, line, round mark, the line under it while it works, and its links.</summary>
sealed class Card : Border, IReport
{
    public readonly App App;
    public bool Picked { get; private set; }
    public bool Locked;
    /// <summary>The copy on this PC was found damaged: the end says "Repaired".</summary>
    public bool Damaged;
    public event Action? Changed;

    readonly Image picture = new() { Width = 46, Height = 46, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock left = new() { FontSize = 12.5, Foreground = Ink.Meta, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
    readonly StackPanel links = new() { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
    readonly TextBlock right = new() { FontSize = 12.5, Foreground = Ink.Soft, VerticalAlignment = VerticalAlignment.Center };
    readonly Panel info = new();
    readonly Border markGlow, markRing;
    readonly Ellipse markFill, markDot;
    readonly Path check;
    readonly Border track, fill, head, glider;
    readonly Panel line;
    CancellationTokenSource? gliding;
    double fraction = -1;

    public Card(App app)
    {
        App = app;
        CornerRadius = new CornerRadius(14);
        BorderThickness = new Thickness(1);
        BorderBrush = Ink.Rule;
        Background = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.Parse("#19191D"), 0), new GradientStop(Color.Parse("#151518"), 1) },
        };
        ClipToBounds = true;
        Margin = new Thickness(0, 0, 0, 12);
        Cursor = new Cursor(StandardCursorType.Hand);
        Focusable = true;
        RenderTransform = TransformOperations.Parse("translateY(0px)");
        Transitions =
        [
            new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(160) },
            new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(160), Easing = new CubicEaseOut() },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) },
        ];

        try { picture.Source = new Bitmap(AssetLoader.Open(new Uri($"avares://GetBradleysApps/Assets/{app.Picture}"))); } catch { }
        picture.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];

        var name = new TextBlock { Text = app.Name, FontSize = 18, Foreground = Ink.Title, FontFamily = Ink.Font };
        var about = new TextBlock { Text = app.Description, FontSize = 13, Foreground = Ink.Soft, TextWrapping = TextWrapping.Wrap, LineHeight = 18.5, Margin = new Thickness(0, 3, 0, 0) };
        var row = new DockPanel { Margin = new Thickness(0, 7, 0, 0), LastChildFill = true };
        DockPanel.SetDock(right, Dock.Right);
        row.Children.Add(right);
        var leftRow = new WrapPanel { Orientation = Orientation.Horizontal };
        leftRow.Children.Add(left);
        leftRow.Children.Add(links);
        row.Children.Add(leftRow);
        info.Children.Add(row);
        info.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        left.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }];
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 16, 0) };
        text.Children.Add(name);
        text.Children.Add(about);
        text.Children.Add(info);

        // the round mark: a ring; picked, it fills in the accent with a dark dot that springs in, and glows a little
        markGlow = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BoxShadow = BoxShadows.Parse("0 0 12 0 #73C38A9B"), Opacity = 0 };
        markRing = new Border { Width = 22, Height = 22, CornerRadius = new CornerRadius(11), BorderThickness = new Thickness(2), BorderBrush = Ink.Ring };
        markFill = new Ellipse { Width = 22, Height = 22, Fill = Ink.Accent, Opacity = 0 };
        markDot = new Ellipse { Width = 8, Height = 8, Fill = Ink.Canvas, RenderTransform = TransformOperations.Parse("scale(0)") };
        foreach (var t in new Visual[] { markGlow, markFill })
            ((Animatable)t).Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }];
        markRing.Transitions = [new BrushTransition { Property = BorderBrushProperty, Duration = TimeSpan.FromMilliseconds(150) }, new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }];
        markDot.Transitions = [new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(240), Easing = new BackEaseOut() }];
        check = new Path
        {
            Data = Geometry.Parse("M5,11.5 L9,15.5 L17.5,6"), Stroke = Ink.Green, StrokeThickness = 2.4,
            StrokeLineCap = PenLineCap.Round, StrokeJoin = PenLineJoin.Round, Width = 22, Height = 22,
            StrokeDashArray = new AvaloniaList<double> { 10, 10 }, StrokeDashOffset = 10, IsVisible = false,
            Transitions = [new DoubleTransition { Property = Shape.StrokeDashOffsetProperty, Duration = TimeSpan.FromMilliseconds(300), Easing = new CubicEaseOut() }],
        };
        var mark = new Panel { Width = 22, Height = 22, VerticalAlignment = VerticalAlignment.Center };
        mark.Children.Add(markGlow); mark.Children.Add(markRing); mark.Children.Add(markFill); mark.Children.Add(markDot); mark.Children.Add(check);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Margin = new Thickness(18, 16, 20, 18) };
        Grid.SetColumn(text, 1); Grid.SetColumn(mark, 2);
        grid.Children.Add(picture); grid.Children.Add(text); grid.Children.Add(mark);

        // the thin line along the bottom edge: fills as bytes arrive, with a lit head; glides while there is no measure
        track = new Border { Background = Ink.Of("#0AFFFFFF") };
        head = new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = Ink.Of("#F3D4E1"), BoxShadow = BoxShadows.Parse("0 0 10 3 #B3D9A3BB"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, -3, -4, -3) };
        var fillPanel = new Panel(); fillPanel.Children.Add(head);
        fill = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Left, Width = 0, Child = fillPanel,
            Background = Gradient("#59C38A9B", "#D9A3BB"),
            Transitions = [new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(180) }],
        };
        glider = new Border { Width = 150, HorizontalAlignment = HorizontalAlignment.Left, IsVisible = false, Background = Gradient("#00D9A3BB", "#D9A3BB", "#00D9A3BB") };
        line = new Panel { Height = 2, VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0, ClipToBounds = false };
        line.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(200) }];
        line.Children.Add(track); line.Children.Add(fill); line.Children.Add(glider);

        var root = new Panel();
        root.Children.Add(grid);
        root.Children.Add(line);
        Child = root;

        PointerEntered += (_, _) => Hover(true);
        PointerExited += (_, _) => Hover(false);
        PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left && !Locked && e.Source is not TextBlock { Tag: "link" }) Toggle(); };
        KeyDown += (_, e) => { if (e.Key is Key.Space && !Locked) { Toggle(); e.Handled = true; } };
        GotFocus += (_, e) => { if (e.NavigationMethod == NavigationMethod.Tab) BorderBrush = Ink.Link; };
        LostFocus += (_, _) => Paint();
    }

    static LinearGradientBrush Gradient(params string[] stops)
    {
        var b = new LinearGradientBrush { StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 0, RelativeUnit.Relative) };
        for (var i = 0; i < stops.Length; i++) b.GradientStops.Add(new GradientStop(Color.Parse(stops[i]), stops.Length == 1 ? 0 : (double)i / (stops.Length - 1)));
        return b;
    }

    bool hovering;
    void Hover(bool on)
    {
        hovering = on && !Locked;
        RenderTransform = TransformOperations.Parse(hovering ? "translateY(-2px)" : "translateY(0px)");
        Paint();
    }

    void Paint() => BorderBrush = Picked && !Locked ? Ink.RulePicked : hovering ? Ink.RuleHover : Ink.Rule;

    public void Toggle() => SetPicked(!Picked);

    public void SetPicked(bool on)
    {
        Picked = on;
        markFill.Opacity = on ? 1 : 0;
        markGlow.Opacity = on ? 1 : 0;
        markRing.BorderBrush = on ? Ink.Accent : Ink.Ring;
        markDot.RenderTransform = TransformOperations.Parse(on ? "scale(1)" : "scale(0)");
        picture.Opacity = on || Locked ? 1 : 0.6;
        Paint();
        Changed?.Invoke();
    }

    public void Lock(bool locked)
    {
        Locked = locked;
        Cursor = new Cursor(locked ? StandardCursorType.Arrow : StandardCursorType.Hand);
        if (locked) Hover(false);
        Paint();
    }

    // ---------------------------------------------------------------- the info line

    public void Say(string words, IBrush? color = null, string rightWords = "", params (string Word, Action Act)[] actions)
    {
        left.Text = words;
        left.Foreground = color ?? Ink.Meta;
        right.Text = rightWords;
        links.Children.Clear();
        foreach (var (word, act) in actions)
        {
            var t = new TextBlock { Text = word, Foreground = Ink.Link, FontSize = 12.5, Cursor = new Cursor(StandardCursorType.Hand), Tag = "link" };
            t.PointerEntered += (_, _) => t.TextDecorations = TextDecorations.Underline;
            t.PointerExited += (_, _) => t.TextDecorations = null;
            t.PointerReleased += (_, e) => { e.Handled = true; act(); };
            links.Children.Add(t);
        }
        links.IsVisible = actions.Length > 0;
    }

    public void FadeInInfo() { left.Opacity = 0; Dispatcher.UIThread.Post(() => left.Opacity = 1, DispatcherPriority.Background); }

    // ---------------------------------------------------------------- IReport (called from any thread)

    long lastHave = -1;
    string downloadWord = "Downloading";
    public void Label(string words) => Dispatcher.UIThread.Post(() => { downloadWord = words; lastHave = -1; });
    public void Downloading(long have, long? total) => Dispatcher.UIThread.Post(() =>
    {
        if (have == lastHave) return;
        lastHave = have;
        StopGliding();
        line.Opacity = 1;
        fill.Background = Gradient("#59C38A9B", "#D9A3BB");
        head.IsVisible = true;
        if (total is > 0 and var t)
        {
            SetFraction((double)have / t);
            Say(downloadWord, Ink.Soft, $"{Mb(have)} of {Mb(t)} MB");
        }
        else Say(downloadWord, Ink.Soft, $"{Mb(have)} MB");
    });

    public void Step(string words) => Dispatcher.UIThread.Post(() =>
    {
        Tag = words == "Installing" ? "installing" : "working";
        lastHave = -1;
        Say(words switch { "Waiting" => "Waiting for the other install to finish", _ => words }, Ink.Soft);
        line.Opacity = 1;
        fill.Width = 0; fraction = -1;
        StartGliding();
    });

    static string Mb(long bytes) => Math.Max(bytes / 1048576.0, 0).ToString(bytes < 10 * 1048576 ? "0.0" : "0");

    void SetFraction(double f)
    {
        fraction = Math.Clamp(f, 0, 1);
        var w = line.Bounds.Width > 0 ? line.Bounds.Width : Bounds.Width;
        fill.Width = w * fraction;
    }

    void StartGliding()
    {
        if (gliding != null) return;
        gliding = new CancellationTokenSource();
        glider.IsVisible = true;
        var w = Math.Max(line.Bounds.Width, 400);
        var anim = new Animation
        {
            Duration = TimeSpan.FromMilliseconds(1300), IterationCount = IterationCount.Infinite, Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(MarginProperty, new Thickness(-150, 0, 0, 0)) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(MarginProperty, new Thickness(w, 0, 0, 0)) } },
            },
        };
        _ = anim.RunAsync(glider, gliding.Token);
    }

    void StopGliding()
    {
        gliding?.Cancel();
        gliding = null;
        glider.IsVisible = false;
    }

    // ---------------------------------------------------------------- endings

    public void Done(string words, params (string, Action)[] actions)
    {
        Tag = "done";
        StopGliding();
        line.Opacity = 0;
        Say(words, Ink.Green, "", actions);
        markFill.Opacity = 0; markGlow.Opacity = 0; markRing.Opacity = 0; markDot.RenderTransform = TransformOperations.Parse("scale(0)");
        check.IsVisible = true;
        Dispatcher.UIThread.Post(() => check.StrokeDashOffset = 0, DispatcherPriority.Background);
    }

    public void Failed(string words, bool amber, params (string, Action)[] actions)
    {
        Tag = "failed";
        StopGliding();
        if (amber || fraction < 0) line.Opacity = 0;
        else
        {
            fill.Background = Gradient("#4DEC8E8E", "#EC8E8E");
            head.IsVisible = false;
        }
        Say(words, amber ? Ink.Amber : Ink.Red, "", actions);
    }

    /// <summary>Back to how it looked before Install (for "Try again").</summary>
    public void Reset()
    {
        Tag = null;
        StopGliding();
        line.Opacity = 0; fill.Width = 0; fraction = -1; lastHave = -1;
        check.IsVisible = false; check.StrokeDashOffset = 10; markRing.Opacity = 1;
        SetPicked(true);
    }
}

public sealed class MainWindow : Window
{
    readonly Card[] cards;
    readonly TextBlock heading = new() { FontSize = 36, FontWeight = FontWeight.Light, Foreground = Ink.Title, FontFamily = Ink.Font, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 6) };
    readonly TextBlock sub = new() { FontSize = 14, Foreground = Ink.Soft, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 24), TextAlignment = TextAlignment.Center };
    readonly Border button;
    readonly TextBlock buttonWord = new() { FontSize = 21, FontFamily = Ink.Font, HorizontalAlignment = HorizontalAlignment.Center };
    readonly Border buttonShine;
    readonly StackPanel foot = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 12, Margin = new Thickness(0, 12, 0, 0), Height = 18 };
    readonly CancellationTokenSource closing = new();
    readonly string work;
    readonly bool testRun = Options.TestFolder != null;
    enum Stage { Choosing, Working, Finished }
    Stage stage = Stage.Choosing;
    Action? primary;
    int running;

    static readonly IBrush ButtonOn = new LinearGradientBrush
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops = { new GradientStop(Color.Parse("#B97E90"), 0), new GradientStop(Color.Parse("#9B6276"), 1) },
    };

    public MainWindow()
    {
        Title = "Get Bradley's apps";
        Width = 600;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaToDecorationsHint = true;
        Background = Ink.Canvas;
        FontFamily = Ink.TextFont;
        RequestedThemeVariant = ThemeVariant.Dark;
        try { Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://GetBradleysApps/Assets/chooser.ico"))); } catch { }

        work = Options.TestFolder ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GetBradleysApps", Environment.ProcessId.ToString());
        cards = [new Card(new Cadence()), new Card(new MusicTool())];

        // the soft rose light at the top
        var glow = new Border
        {
            Height = 300, VerticalAlignment = VerticalAlignment.Top, IsHitTestVisible = false,
            Background = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, -0.25, RelativeUnit.Relative), GradientOrigin = new RelativePoint(0.5, -0.25, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.55, RelativeUnit.Relative), RadiusY = new RelativeScalar(0.95, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Color.Parse("#36C38A9B"), 0), new GradientStop(Color.Parse("#00C38A9B"), 1) },
            },
        };

        // the bar: drag anywhere on it; minimize and close at the right
        var bar = new DockPanel { Height = 36, Background = Brushes.Transparent, LastChildFill = false };
        var close = Glyph("M0,0 L10,10 M10,0 L0,10", () => Close(), Ink.Of("#3A1E24"));
        var minimize = Glyph("M0,5 L10,5", () => WindowState = WindowState.Minimized, Ink.Of("#1E1D22"));
        DockPanel.SetDock(close, Dock.Right); DockPanel.SetDock(minimize, Dock.Right);
        bar.Children.Add(close); bar.Children.Add(minimize);
        bar.PointerPressed += (_, e) => { if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed && e.Source == bar) BeginMoveDrag(e); };

        buttonShine = new Border { CornerRadius = new CornerRadius(10), Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
        buttonShine.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        var face = new Panel(); face.Children.Add(buttonShine); face.Children.Add(new Border { Padding = new Thickness(60, 9, 60, 11), Child = buttonWord });
        button = new Border
        {
            CornerRadius = new CornerRadius(10), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0), Child = face,
            Cursor = new Cursor(StandardCursorType.Hand), RenderTransform = TransformOperations.Parse("scale(1)"),
            Transitions =
            [
                new TransformOperationsTransition { Property = RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(90) },
                new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(180) },
            ],
        };
        button.PointerEntered += (_, _) => { if (primary != null) buttonShine.Opacity = 0.07; };
        button.PointerExited += (_, _) => { buttonShine.Opacity = 0; button.RenderTransform = TransformOperations.Parse("scale(1)"); };
        button.PointerPressed += (_, _) => { if (primary != null) button.RenderTransform = TransformOperations.Parse("scale(0.97)"); };
        button.PointerReleased += (_, e) =>
        {
            button.RenderTransform = TransformOperations.Parse("scale(1)");
            if (e.InitialPressMouseButton == MouseButton.Left && button.IsPointerOver) primary?.Invoke();
        };

        var body = new StackPanel { Margin = new Thickness(44, 0, 44, 26) };
        heading.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        sub.Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(120) }];
        body.Children.Add(heading); body.Children.Add(sub);
        foreach (var c in cards) { body.Children.Add(c); c.Changed += Refresh; }
        body.Children.Add(button);
        body.Children.Add(foot);

        var layout = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        layout.Children.Add(bar); layout.Children.Add(body);
        var root = new Panel();
        root.Children.Add(glow); root.Children.Add(layout);
        Content = root;

        heading.Text = "Bradley's apps";
        sub.Text = "Pick what you'd like. Each one keeps itself up to date.";
        foreach (var c in cards) { c.SetPicked(true); c.Say("Looking for the newest version"); }

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && primary != null) { primary(); e.Handled = true; }
            else if (e.Key == Key.Escape && stage != Stage.Working) Close();
        };
        Closing += OnClosing;
        Opened += async (_, _) =>
        {
            if (Options.ShotState != null) { await Shot.Take(this, cards, Options.ShotState, Options.ShotFile!); return; }
            await Start();
            // --auto (only with --test-run): presses Install once the newest versions are known, for an unattended test
            if (testRun && Environment.GetCommandLineArgs().Contains("--auto")) { await Task.Delay(600); foreach (var c in cards) c.SetPicked(true); Install(); }
        };
        Refresh();
    }

    Border Glyph(string data, Action act, IBrush hover)
    {
        var path = new Path { Data = Geometry.Parse(data), Stroke = Ink.Of("#6B6873"), StrokeThickness = 1.2, Width = 10, Height = 10, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var b = new Border { Width = 46, Height = 36, Background = Brushes.Transparent, Child = path };
        b.Transitions = [new BrushTransition { Property = Border.BackgroundProperty, Duration = TimeSpan.FromMilliseconds(100) }];
        b.PointerEntered += (_, _) => { b.Background = hover; path.Stroke = Ink.Title; };
        b.PointerExited += (_, _) => { b.Background = Brushes.Transparent; path.Stroke = Ink.Of("#6B6873"); };
        b.PointerReleased += (_, e) => { if (e.InitialPressMouseButton == MouseButton.Left && b.IsPointerOver) act(); };
        return b;
    }

    // ---------------------------------------------------------------- the words at the top, the button, the line under it

    public void SetHeading(string h, string s)
    {
        if (heading.Text == h && sub.Text == s) return;
        heading.Opacity = 0; sub.Opacity = 0;
        DispatcherTimer.RunOnce(() => { heading.Text = h; sub.Text = s; heading.Opacity = 1; sub.Opacity = 1; }, TimeSpan.FromMilliseconds(120));
    }

    /// <summary>The one loud word: <paramref name="act"/> null shows it dimmed; <paramref name="quiet"/> shows a plain word.</summary>
    public void SetButton(string? word, Action? act, bool quiet = false)
    {
        button.Opacity = word != null ? 1 : 0;
        button.IsHitTestVisible = word != null;
        buttonWord.Text = word ?? "";
        primary = act;
        var loud = act != null && !quiet;
        button.Background = loud ? ButtonOn : quiet ? Brushes.Transparent : Ink.Of("#26242A");
        button.BoxShadow = loud ? BoxShadows.Parse("0 6 22 0 #47C38A9B") : default;
        buttonWord.Foreground = loud ? Ink.Of("#FFF6FA") : quiet ? Ink.Soft : Ink.Of("#8D8794");
        buttonWord.FontSize = quiet ? 17 : 21;
        button.Cursor = new Cursor(act != null ? StandardCursorType.Hand : StandardCursorType.Arrow);
    }

    public void SetFoot(string words, params (string Word, Action Act)[] actions)
    {
        foot.Children.Clear();
        foot.Children.Add(new TextBlock { Text = words, FontSize = 12, Foreground = Ink.Meta });
        foreach (var (word, act) in actions)
        {
            var t = new TextBlock { Text = word, FontSize = 12, Foreground = Ink.Link, Cursor = new Cursor(StandardCursorType.Hand) };
            t.PointerEntered += (_, _) => t.TextDecorations = TextDecorations.Underline;
            t.PointerExited += (_, _) => t.TextDecorations = null;
            t.PointerReleased += (_, _) => act();
            foot.Children.Add(t);
        }
    }

    void Refresh()
    {
        if (stage != Stage.Choosing) return;
        var any = cards.Any(c => c.Picked);
        SetButton("Install", any ? Install : null);
        if (testRun) SetFoot("Test run: downloads and checks only, installs nothing.");
        else if (!any && cards.All(c => c.App.Here != null)) SetFoot("Pick one to install the newest version over it.");
        else SetFoot(any ? "No admin rights needed." : "Pick at least one.");
    }

    // ---------------------------------------------------------------- what happens

    async Task Start()
    {
        CleanOldWork();
        await Task.WhenAll(cards.Select(c => Task.Run(c.App.Look)));
        foreach (var c in cards) c.SetPicked(c.App.Here == null);
        Refresh();
        await Task.WhenAll(cards.Select(Describe));
    }

    /// <summary>The line under an app's description once the newest release is known.</summary>
    async Task Describe(Card c)
    {
        var app = c.App;
        try
        {
            var r = await app.Newest(closing.Token);
            if (stage != Stage.Choosing) return;
            var size = app is Cadence ? r.Find(n => n.EndsWith("Setup.exe", StringComparison.OrdinalIgnoreCase))?.Size
                : (r.Find(n => n.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) ?? r.Find(n => n.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))?.Size;
            var sized = size is > 0 and var s ? $", {Math.Max(1, Math.Round(s / 1048576.0)):0} MB" : "";
            if (app.Here is { } h)
            {
                var where = Where(h.Folder);
                var at = where == null ? "On this PC" : $"On this PC in {where}";
                var damage = await app.Damage(closing.Token);
                if (stage != Stage.Choosing) return;
                c.Damaged = damage != null;
                if (damage != null)
                {
                    // something is wrong with it: picked, so Install puts it right (settings and library stay as they are)
                    c.SetPicked(true);
                    c.Say($"{at}, but {damage}. Install repairs it.", Ink.Amber);
                }
                else c.Say(h.Version == null ? $"{at}. It keeps itself up to date."
                    : Releases.Compare(h.Version, r.Version) >= 0 ? $"{at}, version {h.Version}, the newest."
                    : $"{at}, version {h.Version}. It will update itself to {r.Version}.");
            }
            else
            {
                // the Music Tool brings ffmpeg along when this PC has none
                if (app is MusicTool tool && await Task.Run(MusicTool.FfmpegMissing))
                {
                    var pin = await tool.FfmpegPin(closing.Token);
                    sized += $", plus ffmpeg ({Math.Round(pin.Size / 1048576.0):0} MB)";
                }
                if (stage != Stage.Choosing) return;
                c.Say($"Version {r.Version}{sized}");
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            if (stage != Stage.Choosing) return;
            c.Say(e is Problem ? e.Message : "Couldn't reach GitHub. Check your internet connection.", Ink.Red, "", ("Try again", () => { c.Say("Looking for the newest version"); _ = Describe(c); }));
        }
        catch (OperationCanceledException) { return; }
        c.FadeInInfo();
    }

    /// <summary>A short name for where the Music Tool lives when it is not where this puts it ("Documents"), or null.</summary>
    static string? Where(string folder)
    {
        if (string.Equals(folder.TrimEnd('\\'), MusicTool.StandardFolder, StringComparison.OrdinalIgnoreCase)) return null;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (folder.StartsWith(local, StringComparison.OrdinalIgnoreCase)) return null;
        foreach (var (special, name) in new[] { (Environment.SpecialFolder.MyDocuments, "Documents"), (Environment.SpecialFolder.DesktopDirectory, "Desktop"), (Environment.SpecialFolder.UserProfile, null as string) })
        {
            var p = Environment.GetFolderPath(special);
            if (p.Length > 0 && folder.StartsWith(p + "\\", StringComparison.OrdinalIgnoreCase) || folder.Equals(p, StringComparison.OrdinalIgnoreCase))
                return name ?? (folder.Length > p.Length ? folder[(p.Length + 1)..].Split('\\')[0] : null);
        }
        return System.IO.Path.GetFileName(folder.TrimEnd('\\'));
    }

    async void Install()
    {
        if (stage != Stage.Choosing) return;
        var picked = cards.Where(c => c.Picked).ToList();
        if (picked.Count == 0) return;
        primary = null;
        // an open app is named before anything starts, so nothing is left half done
        var open = (await Task.Run(() => picked.Where(c => { c.App.Look(); return c.App.IsRunning(); }).ToList()));
        if (open.Count > 0 && !testRun)
        {
            foreach (var c in open) c.Say($"Close {c.App.ShortName} first, then click Install.", Ink.Amber);
            Refresh();
            return;
        }
        stage = Stage.Working;
        foreach (var c in cards) { c.Lock(true); if (!c.Picked) c.Opacity = 0.45; }
        foreach (var c in picked) _ = Run(c);
    }

    async Task Run(Card c)
    {
        Interlocked.Increment(ref running);
        stage = Stage.Working;
        SetHeading("Installing", "You can keep using your PC. This takes about a minute.");
        SetButton(null, null);
        SetFoot("");
        c.Say("Starting", Ink.Soft);
        try
        {
            Directory.CreateDirectory(work);
            var appWork = System.IO.Path.Combine(work, c.App.GetType().Name);
            Directory.CreateDirectory(appWork);
            var report = new Watch(c);
            var version = await Task.Run(() => c.App.Install(appWork, report, testRun, closing.Token));
            var note = c.App.Note is { } n ? " " + n : "";
            if (testRun) c.Done($"Checked, version {version}.{note} Not installed (test run).");
            else c.Done($"{(c.Damaged ? "Repaired" : "Installed")}, version {version}.{note}", ("Open", () => Open(c.App)));
            Log.Write($"{c.App.Name}: done, {version}");
        }
        catch (OperationCanceledException) { return; }
        catch (NeedsClosing e)
        {
            Log.Write($"{c.App.Name}: {e.Message}");
            c.Failed(e.Message, true, ("Try again", () => Again(c)));
        }
        catch (Problem p)
        {
            Log.Write($"{c.App.Name}: {p.Message} {p.InnerException?.Message}");
            var acts = new List<(string, Action)>();
            if (p.Retry) acts.Add(("Try again", () => Again(c)));
            if (p.Page || !p.Retry) acts.Add(("Download page", () => Browse(c.App.Page)));
            c.Failed(p.Message, false, acts.ToArray());
        }
        catch (Exception e)
        {
            Log.Write($"{c.App.Name}: unexpected: {e}");
            c.Failed("Something went wrong.", false, ("Try again", () => Again(c)), ("Download page", () => Browse(c.App.Page)));
        }
        finally
        {
            if (Interlocked.Decrement(ref running) == 0) Finish();
        }
    }

    void Again(Card c)
    {
        if (closing.IsCancellationRequested) return;
        c.Reset();
        c.Lock(true);
        _ = Run(c);
    }

    void Finish()
    {
        stage = Stage.Finished;
        var picked = cards.Where(c => c.Picked).ToList();
        var ok = picked.Count(c => c.Tag is "done");
        var all = picked.Count;
        if (ok == all)
        {
            SetHeading("All set", testRun ? "Test run: downloaded and checked, nothing installed." : all == 1 ? "You'll find it in your Start menu." : "You'll find both in your Start menu.");
            SetButton("Close", Close);
            SetFoot("");
        }
        else
        {
            SetHeading(ok == 0 ? "Not installed" : "Almost there", ok == 0 ? (all == 1 ? "It didn't finish." : "Neither one finished.") : "One didn't finish. The other is installed.");
            SetButton("Close", Close, quiet: true);
            SetFoot("", ("See what happened", () => Browse(Log.Path)));
        }
    }

    sealed class Watch(Card c) : IReport
    {
        public void Downloading(long have, long? total) => c.Downloading(have, total);
        public void Step(string words) => c.Step(words);
        public void Label(string words) => c.Label(words);
    }

    static void Open(App app)
    {
        if (app.Here is not { } h) return;
        try { Process.Start(new ProcessStartInfo(h.Exe) { UseShellExecute = true, WorkingDirectory = h.Folder })?.Dispose(); }
        catch (Exception e) { Log.Write($"open {h.Exe}: {e.Message}"); }
    }

    static void Browse(string target)
    {
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose(); }
        catch (Exception e) { Log.Write($"open {target}: {e.Message}"); }
    }

    void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        // an installer that has started is never cut off halfway
        if (stage == Stage.Working && cards.Any(c => c.Tag is "installing"))
        {
            e.Cancel = true;
            SetHeading("Almost done", "Closing is safe in a moment.");
            return;
        }
        closing.Cancel();
        if (!testRun) { try { Directory.Delete(work, true); } catch { } }
    }

    /// <summary>Leftovers of earlier runs that were stopped hard (a day old or more).</summary>
    void CleanOldWork()
    {
        if (testRun) return;
        try
        {
            var parent = System.IO.Path.GetDirectoryName(work)!;
            if (!Directory.Exists(parent)) return;
            foreach (var d in Directory.GetDirectories(parent))
                if (Directory.GetLastWriteTimeUtc(d) < DateTime.UtcNow.AddDays(-1)) try { Directory.Delete(d, true); } catch { }
        }
        catch { }
    }
}
