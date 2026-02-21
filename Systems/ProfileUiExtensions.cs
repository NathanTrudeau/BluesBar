using System.Windows.Media;

namespace BluesBar.Systems
{
    public static class ProfileUiExtensions
    {
        public static Brush PlayerNameBrush(this Profile p)
        {
            try
            {
                var obj = ColorConverter.ConvertFromString(p.PlayerNameColorHex);
                if (obj is Color c)
                {
                    var b = new SolidColorBrush(c);
                    b.Freeze();
                    return b;
                }
            }
            catch { }

            return Brushes.DarkSlateBlue;
        }
    }
}


