using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace Cursor2Windows;

public enum MiniTetrisAction { Left, Right, Down, Rotate, Drop, Reset }

public sealed class MiniTetrisView : StackPanel
{
    private const int Columns = 10;
    private const int Rows = 16;
    private static event Action<MiniTetrisAction>? ActionPosted;
    private readonly int?[,] _board = new int?[Rows, Columns];
    private readonly WpfRectangle[,] _cells = new WpfRectangle[Rows, Columns];
    private readonly TextBlock _scoreText = new();
    private readonly DispatcherTimer _gravity = new() { Interval = TimeSpan.FromMilliseconds(360) };
    private Piece _piece = Piece.Random();
    private int _score;
    private int _highScore;

    public MiniTetrisView(OverlayState state)
    {
        Orientation = Orientation.Vertical;
        Visibility = Visibility.Collapsed;
        _scoreText.FontSize = 8;
        _scoreText.FontWeight = FontWeights.Bold;
        _scoreText.Foreground = new SolidColorBrush(Color.FromArgb(174, 255, 255, 255));
        Children.Add(_scoreText);
        var grid = new UniformGrid { Columns = Columns, Rows = Rows, Margin = new Thickness(0, 4, 0, 0) };
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
        {
            var cell = new WpfRectangle { Width = 8.5, Height = 8.5, RadiusX = 1.8, RadiusY = 1.8, Margin = new Thickness(.75) };
            _cells[y, x] = cell;
            grid.Children.Add(cell);
        }
        Children.Add(grid);
        _gravity.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) => { if (IsVisible) _gravity.Start(); else _gravity.Stop(); };
        ActionPosted += Handle;
        Render();
    }

    public static void Post(MiniTetrisAction action) => ActionPosted?.Invoke(action);

    private void Handle(MiniTetrisAction action)
    {
        if (!IsVisible) return;
        switch (action)
        {
            case MiniTetrisAction.Left: Move(-1); break;
            case MiniTetrisAction.Right: Move(1); break;
            case MiniTetrisAction.Down: Tick(); break;
            case MiniTetrisAction.Rotate: Rotate(); break;
            case MiniTetrisAction.Drop: HardDrop(); break;
            case MiniTetrisAction.Reset: Reset(); break;
        }
        Render();
    }

    private void Move(int dx)
    {
        var moved = _piece with { X = _piece.X + dx };
        if (CanPlace(moved)) _piece = moved;
    }

    private void Rotate()
    {
        var rotated = _piece.Rotated();
        if (CanPlace(rotated)) { _piece = rotated; return; }
        foreach (var kick in new[] { -1, 1, -2, 2 })
        {
            var kicked = rotated with { X = rotated.X + kick };
            if (CanPlace(kicked)) { _piece = kicked; return; }
        }
    }

    private void HardDrop()
    {
        while (true)
        {
            var moved = _piece with { Y = _piece.Y + 1 };
            if (!CanPlace(moved)) break;
            _piece = moved;
        }
        Tick();
    }

    private void Tick()
    {
        var moved = _piece with { Y = _piece.Y + 1 };
        if (CanPlace(moved)) { _piece = moved; Render(); return; }
        foreach (var (x, y) in _piece.Cells()) if (Inside(x, y)) _board[y, x] = _piece.ColorIndex;
        ClearRows();
        var next = Piece.Random();
        if (CanPlace(next)) _piece = next;
        else Reset();
        Render();
    }

    private void ClearRows()
    {
        var write = Rows - 1;
        var cleared = 0;
        for (var y = Rows - 1; y >= 0; y--)
        {
            var full = Enumerable.Range(0, Columns).All(x => _board[y, x].HasValue);
            if (full) { cleared++; continue; }
            for (var x = 0; x < Columns; x++) _board[write, x] = _board[y, x];
            write--;
        }
        for (var y = write; y >= 0; y--) for (var x = 0; x < Columns; x++) _board[y, x] = null;
        if (cleared > 0)
        {
            _score += cleared * 100;
            _highScore = Math.Max(_highScore, _score);
        }
    }

    private bool CanPlace(Piece piece) => piece.Cells().All(c => Inside(c.X, c.Y) && !_board[c.Y, c.X].HasValue);
    private static bool Inside(int x, int y) => x >= 0 && x < Columns && y >= 0 && y < Rows;

    private void Reset()
    {
        Array.Clear(_board);
        _piece = Piece.Random();
        _score = 0;
    }

    private void Render()
    {
        _scoreText.Text = $"tetris {_score} (hi: {_highScore})";
        var active = _piece.Cells().ToHashSet();
        for (var y = 0; y < Rows; y++)
        for (var x = 0; x < Columns; x++)
        {
            var index = active.Contains((x, y)) ? _piece.ColorIndex : _board[y, x];
            var color = index.HasValue ? OverlayState.Palette[index.Value % OverlayState.Palette.Length] : Color.FromArgb(20, 255, 255, 255);
            _cells[y, x].Fill = new SolidColorBrush(color) { Opacity = index.HasValue ? .9 : 1 };
            _cells[y, x].Stroke = index.HasValue ? new SolidColorBrush(Color.FromArgb(120, 255, 255, 255)) : Brushes.Transparent;
        }
    }

    private sealed record Piece((int X, int Y)[] Offsets, int X, int Y, int ColorIndex)
    {
        public IEnumerable<(int X, int Y)> Cells() => Offsets.Select(o => (X + o.X, Y + o.Y));
        public Piece Rotated() => this with { Offsets = Offsets.Select(o => (-o.Y, o.X)).ToArray() };
        public static Piece Random()
        {
            var shapes = new[]
            {
                new[] { (-1,0), (0,0), (1,0), (2,0) },
                new[] { (0,0), (1,0), (0,1), (1,1) },
                new[] { (-1,0), (0,0), (1,0), (0,1) },
                new[] { (0,0), (1,0), (-1,1), (0,1) },
                new[] { (-1,0), (0,0), (0,1), (1,1) },
                new[] { (-1,0), (0,0), (1,0), (-1,1) },
                new[] { (-1,0), (0,0), (1,0), (1,1) }
            };
            var index = System.Random.Shared.Next(shapes.Length);
            return new Piece(shapes[index], 4, 1, index);
        }
    }
}
