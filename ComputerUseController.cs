using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

namespace Cursor2Windows;

public sealed record ScreenSnapshot(string ImageDataUrl, Rectangle Bounds, int GridColumns, int GridRows, Bitmap Image);
public sealed record ClickTarget(string Cell, double X, double Y, double? Confidence);

public static class ComputerUseController
{
    private const int GridColumns = 12;
    private const int GridRows = 8;
    public const int DetailGridColumns = 10;
    public const int DetailGridRows = 10;

    public static ScreenSnapshot CaptureMainDisplay()
    {
        var bounds = Forms.Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, NativeMethods.GetSystemMetrics(0), NativeMethods.GetSystemMetrics(1));
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.CopyFromScreen(bounds.Left, bounds.Top, 0, 0, bounds.Size);
        }
        return new ScreenSnapshot(GriddedJpegDataUrl(bitmap, GridColumns, GridRows), bounds, GridColumns, GridRows, bitmap);
    }

    public static void Click(ClickTarget target, ScreenSnapshot snapshot)
    {
        var ratio = NormalizedPoint(target, snapshot.GridColumns, snapshot.GridRows);
        var x = snapshot.Bounds.Left + snapshot.Bounds.Width * ratio.X;
        var y = snapshot.Bounds.Top + snapshot.Bounds.Height * ratio.Y;
        SendMouse(x, y, NativeMethods.MOUSEEVENTF_MOVE);
        SendMouse(x, y, NativeMethods.MOUSEEVENTF_LEFTDOWN);
        Thread.Sleep(80);
        SendMouse(x, y, NativeMethods.MOUSEEVENTF_LEFTUP);
    }

    public static string DetailImageDataUrl(ClickTarget target, ScreenSnapshot snapshot)
    {
        var address = CellAddress(target.Cell, snapshot.GridColumns, snapshot.GridRows);
        var crop = new Rectangle(
            snapshot.Image.Width * address.Column / snapshot.GridColumns,
            snapshot.Image.Height * address.Row / snapshot.GridRows,
            snapshot.Image.Width / snapshot.GridColumns,
            snapshot.Image.Height / snapshot.GridRows);
        using var cropped = snapshot.Image.Clone(crop, snapshot.Image.PixelFormat);
        return GriddedJpegDataUrl(cropped, DetailGridColumns, DetailGridRows);
    }

    public static ClickTarget RefinedTarget(ClickTarget coarse, ClickTarget fine)
    {
        var address = CellAddress(fine.Cell, DetailGridColumns, DetailGridRows);
        if (fine.X is < 0 or > 1 || fine.Y is < 0 or > 1) throw new InvalidOperationException("bro idk where to click");
        var x = (address.Column + fine.X) / DetailGridColumns;
        var y = (address.Row + fine.Y) / DetailGridRows;
        return new ClickTarget(coarse.Cell, x, y, fine.Confidence ?? coarse.Confidence);
    }

    private static PointF NormalizedPoint(ClickTarget target, int columns, int rows)
    {
        var address = CellAddress(target.Cell, columns, rows);
        if (target.X is < 0 or > 1 || target.Y is < 0 or > 1) throw new InvalidOperationException("bro idk where to click");
        return new PointF((float)((address.Column + target.X) / columns), (float)((address.Row + target.Y) / rows));
    }

    private static (int Column, int Row) CellAddress(string cell, int columns, int rows)
    {
        var cleaned = cell.Trim().ToUpperInvariant();
        if (cleaned.Length < 2 || cleaned[0] < 'A' || cleaned[0] > 'Z') throw new InvalidOperationException("bro idk where to click");
        var column = cleaned[0] - 'A';
        if (!int.TryParse(cleaned[1..], out var rowNumber) || column < 0 || column >= columns || rowNumber < 1 || rowNumber > rows)
        {
            throw new InvalidOperationException("bro idk where to click");
        }
        return (column, rowNumber - 1);
    }

    private static string GriddedJpegDataUrl(Bitmap source, int columns, int rows)
    {
        var scale = Math.Min(1.0, 1280.0 / Math.Max(source.Width, source.Height));
        using var bitmap = new Bitmap((int)(source.Width * scale), (int)(source.Height * scale));
        using (var g = Graphics.FromImage(bitmap))
        {
            g.DrawImage(source, 0, 0, bitmap.Width, bitmap.Height);
            DrawGrid(g, bitmap.Size, columns, rows);
        }
        using var stream = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, 48L);
        bitmap.Save(stream, codec, parameters);
        return "data:image/jpeg;base64," + Convert.ToBase64String(stream.ToArray());
    }

    private static void DrawGrid(Graphics g, Size size, int columns, int rows)
    {
        var cellWidth = size.Width / (float)columns;
        var cellHeight = size.Height / (float)rows;
        using var shadow = new Pen(Color.FromArgb(158, 0, 0, 0), 3);
        using var line = new Pen(Color.FromArgb(143, 255, 255, 255), 1);
        foreach (var pen in new[] { shadow, line })
        {
            for (var c = 0; c <= columns; c++) g.DrawLine(pen, c * cellWidth, 0, c * cellWidth, size.Height);
            for (var r = 0; r <= rows; r++) g.DrawLine(pen, 0, r * cellHeight, size.Width, r * cellHeight);
        }
        using var font = new Font(FontFamily.GenericMonospace, Math.Max(12, Math.Min(18, Math.Min(cellWidth, cellHeight) * .18f)), FontStyle.Bold);
        for (var row = 0; row < rows; row++)
        for (var column = 0; column < columns; column++)
        {
            var label = $"{(char)('A' + column)}{row + 1}";
            var pos = new PointF(column * cellWidth + 6, row * cellHeight + 6);
            var labelSize = g.MeasureString(label, font);
            g.FillRectangle(new SolidBrush(Color.FromArgb(174, 0, 0, 0)), pos.X - 4, pos.Y - 2, labelSize.Width + 8, labelSize.Height + 4);
            g.DrawString(label, font, Brushes.White, pos);
        }
    }

    private static void SendMouse(double x, double y, uint flags)
    {
        var width = NativeMethods.GetSystemMetrics(0);
        var height = NativeMethods.GetSystemMetrics(1);
        var input = new NativeMethods.INPUT
        {
            type = NativeMethods.INPUT_MOUSE,
            mi = new NativeMethods.MOUSEINPUT
            {
                dx = (int)Math.Round(x * 65535 / Math.Max(1, width - 1)),
                dy = (int)Math.Round(y * 65535 / Math.Max(1, height - 1)),
                dwFlags = flags | NativeMethods.MOUSEEVENTF_ABSOLUTE
            }
        };
        NativeMethods.SendInput(1, new[] { input }, Marshal.SizeOf<NativeMethods.INPUT>());
    }
}
