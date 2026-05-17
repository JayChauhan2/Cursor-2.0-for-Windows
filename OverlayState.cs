using MediaColor = System.Windows.Media.Color;
using MediaColors = System.Windows.Media.Colors;

namespace Cursor2Windows;

public sealed class OverlayState
{
    public static readonly MediaColor[] Palette =
    {
        MediaColors.Cyan,
        MediaColors.MediumPurple,
        MediaColors.LimeGreen,
        MediaColors.Gold,
        MediaColors.Orange,
        MediaColors.DeepPink,
        MediaColor.FromRgb(26, 255, 128)
    };

    public event Action? Changed;
    public bool IsVisible { get; set; }
    public MediaColor CursorColor { get; set; } = MediaColors.DodgerBlue;
    public double AudioLevel { get; set; }
    public string ResponseText { get; set; } = "";
    public string UserText { get; set; } = "";
    public string SubmittedText { get; set; } = "";
    public bool IsListening { get; set; }
    public bool IsProcessing { get; set; }
    public bool IsSending { get; set; }
    public bool ShowsTetris { get; set; }
    public bool ShowsWaveform => !ShowsTetris && string.IsNullOrEmpty(UserText) && (IsListening || IsProcessing || string.IsNullOrEmpty(ResponseText));
    public void Notify() => Changed?.Invoke();
}
