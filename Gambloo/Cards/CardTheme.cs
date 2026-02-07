using System.Windows.Media;

namespace BluesBar.Gambloo.Cards
{
    /// <summary>
    /// Theme values for rendering cards. Later you can replace rendering with images.
    /// </summary>
    public sealed class CardTheme
    {
        public string ThemeId { get; init; } = "bluebar-default";
        public string DisplayName { get; init; } = "BlueBar Default";

        public Brush CardFace { get; init; } = new SolidColorBrush(Color.FromRgb(0xF3, 0xF4, 0xF6));
        public Brush CardBack { get; init; } = new SolidColorBrush(Color.FromRgb(0x10, 0x2A, 0x5C));
        public Brush CardBorder { get; init; } = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));

        public Brush TextBlack { get; init; } = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
        public Brush TextRed { get; init; } = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));

        // sizing
        public double CardWidth { get; set; } = 120;
        public double CardHeight { get; set; } = 170;
        public double CornerRadius { get; init; } = 14;
        public double BorderThickness { get; init; } = 2;

        // colors (blueback vibe)
        public Brush FaceBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0xF7, 0xFB, 0xFF));
        public Brush FaceBorder { get; set; } = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
        public Brush BackBackground { get; set; } = new SolidColorBrush(Color.FromRgb(0x0E, 0x2A, 0x55));
        public Brush BackBorder { get; set; } = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
        public Brush PipRed { get; set; } = new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C));
        public Brush PipBlack { get; set; } = new SolidColorBrush(Color.FromRgb(0x10, 0x18, 0x2A));

        // animation
        public int DealMs { get; set; } = 140;
        public int FlipMs { get; set; } = 160;
    }

    public static class CardThemes
    {
        // One place to swap default later
        public static CardTheme Default { get; } = new CardTheme();
    }
}

