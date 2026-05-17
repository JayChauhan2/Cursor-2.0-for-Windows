using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfPath = System.Windows.Shapes.Path;

namespace Cursor2Windows;

public partial class MainWindow : Window
{
    private readonly OverlayState _state = new();
    private readonly VoicePromptManager _voice;
    private readonly GlobalInputManager _input;
    private readonly DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(80) };
    private readonly Random _random = new();
    private Border _bubble = null!;
    private TextBlock _submittedText = null!;
    private TextBlock _userText = null!;
    private TextBlock _responseText = null!;
    private StackPanel _waveform = null!;
    private MiniTetrisView _tetris = null!;
    private WpfPath _cursor = null!;
    private WpfPath _check = null!;

    public MainWindow()
    {
        InitializeComponent();
        _voice = new VoicePromptManager(_state, Dispatcher);
        _input = new GlobalInputManager(_state, _voice, Dispatcher, MoveOverlayNearCursor, ShutdownApp);
        BuildUi();
        Loaded += OnLoaded;
        Closed += (_, _) => _input.Dispose();
        _state.Changed += Render;
        _renderTimer.Tick += (_, _) => Render();
        _renderTimer.Start();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Left = -10000;
        Top = -10000;
        _input.Start();
        MakeClickThrough();
    }

    private void BuildUi()
    {
        var shell = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Bottom,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness(6)
        };

        var cursorHost = new Grid { Width = 42, Height = 42, Margin = new Thickness(0, 0, 8, 0) };
        _cursor = new WpfPath { Data = Geometry.Parse("M 0 0 L 28 28 L 18 28 L 24 40 L 15 40 L 10 28 L 0 38 Z"), Stretch = Stretch.Uniform, Width = 18, Height = 26, Opacity = .58 };
        _check = new WpfPath { Data = Geometry.Parse("M 2 14 L 9 21 L 24 3"), Stretch = Stretch.Uniform, StrokeThickness = 3, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, Visibility = Visibility.Collapsed };
        cursorHost.Children.Add(_cursor);
        cursorHost.Children.Add(_check);

        _waveform = new StackPanel { Orientation = Orientation.Horizontal, Height = 22, Margin = new Thickness(0, 0, 0, 4) };
        for (var i = 0; i < 5; i++)
        {
            _waveform.Children.Add(new Border { Width = 4, Height = 5, CornerRadius = new CornerRadius(2), Background = Brushes.White, Opacity = .75, Margin = new Thickness(1) });
        }

        _tetris = new MiniTetrisView(_state);
        _submittedText = new TextBlock { FontSize = 9, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)), TextWrapping = TextWrapping.Wrap, MaxWidth = 180, Margin = new Thickness(0, 0, 0, 3) };
        _userText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxWidth = 180 };
        _responseText = new TextBlock { FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, MaxWidth = 180 };

        var textStack = new StackPanel();
        textStack.Children.Add(_waveform);
        textStack.Children.Add(_tetris);
        textStack.Children.Add(_submittedText);
        textStack.Children.Add(_userText);
        textStack.Children.Add(_responseText);

        _bubble = new Border
        {
            MinWidth = 44,
            MaxWidth = 190,
            Padding = new Thickness(7, 5, 7, 5),
            CornerRadius = new CornerRadius(7),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromArgb(46, 255, 255, 255)),
            Background = new SolidColorBrush(Color.FromArgb(184, 0, 0, 0)),
            Child = textStack
        };

        shell.Children.Add(cursorHost);
        shell.Children.Add(_bubble);
        Root.Children.Add(shell);
    }

    private void Render()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var color = _state.CursorColor;
            _cursor.Fill = new SolidColorBrush(color);
            _cursor.Stroke = new SolidColorBrush(Color.FromArgb(190, 255, 255, 255));
            _check.Stroke = new SolidColorBrush(color);
            _cursor.Visibility = _state.IsSending ? Visibility.Collapsed : Visibility.Visible;
            _check.Visibility = _state.IsSending ? Visibility.Visible : Visibility.Collapsed;
            _bubble.Opacity = _state.IsVisible ? 1 : 0;
            Root.Opacity = _state.IsVisible ? 1 : 0;
            _submittedText.Text = string.IsNullOrWhiteSpace(_state.SubmittedText) ? "" : $"sent: {_state.SubmittedText}";
            _submittedText.Visibility = string.IsNullOrWhiteSpace(_state.SubmittedText) ? Visibility.Collapsed : Visibility.Visible;
            _userText.Text = _state.UserText;
            _userText.Foreground = new SolidColorBrush(color);
            _responseText.Text = _state.ResponseText;
            _tetris.Visibility = _state.ShowsTetris ? Visibility.Visible : Visibility.Collapsed;
            _waveform.Visibility = _state.ShowsWaveform ? Visibility.Visible : Visibility.Collapsed;
            RenderWaveform();
            if (!_state.IsVisible)
            {
                Left = -10000;
                Top = -10000;
            }
        });
    }

    private void RenderWaveform()
    {
        for (var i = 0; i < _waveform.Children.Count; i++)
        {
            var bar = (Border)_waveform.Children[i];
            var centerBoost = 1 - Math.Abs(i - 2) * .18;
            bar.Height = _state.ShowsWaveform ? 4 + (5 + i % 3 * 4) * Math.Max(.04, _state.AudioLevel) * centerBoost : 4;
            bar.Opacity = _state.ShowsWaveform ? .95 : .62;
        }
    }

    private void MoveOverlayNearCursor(int x, int y)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_state.IsVisible) return;
            var source = PresentationSource.FromVisual(this);
            var transform = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var point = transform.Transform(new Point(x, y));
            Left = point.X + 10;
            Top = point.Y - 18;
        });
    }

    private void ShutdownApp()
    {
        _input.Dispose();
        Application.Current.Shutdown();
    }

    private void MakeClickThrough()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var styles = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, styles | NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_TOOLWINDOW);
    }

    public void RandomizeCursorColor()
    {
        _state.CursorColor = OverlayState.Palette[_random.Next(OverlayState.Palette.Length)];
        _state.Notify();
    }
}
