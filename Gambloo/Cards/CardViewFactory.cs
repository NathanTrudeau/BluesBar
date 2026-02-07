using BluesBar.Gambloo.Cards;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace BluesBar.Gambloo.Cards
{
    public static class CardViewFactory
    {
        public sealed class CardVisual
        {
            public Border Root { get; init; } = null!;
            public Grid FaceLayer { get; init; } = null!;
            public Grid BackLayer { get; init; } = null!;
            public ScaleTransform FlipScale { get; init; } = null!;
            public TranslateTransform DealTranslate { get; init; } = null!;
            public bool IsFaceUp { get; private set; }

            public void SetFaceUp(bool faceUp)
            {
                IsFaceUp = faceUp;
                FaceLayer.Visibility = faceUp ? Visibility.Visible : Visibility.Hidden;
                BackLayer.Visibility = faceUp ? Visibility.Hidden : Visibility.Visible;
            }
        }

        public static CardVisual CreateCard(CardTheme theme, Card card, bool startFaceUp)
        {
            var flipScale = new ScaleTransform(1, 1);
            var dealTranslate = new TranslateTransform(0, 0);

            var tg = new TransformGroup();
            tg.Children.Add(flipScale);
            tg.Children.Add(dealTranslate);

            var root = new Border
            {
                Width = theme.CardWidth,
                Height = theme.CardHeight,
                CornerRadius = new CornerRadius(theme.CornerRadius),
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = tg,
                Margin = new Thickness(10, 0, 10, 0),
                SnapsToDevicePixels = true
            };

            var host = new Grid();
            root.Child = host;

            var back = BuildBack(theme);
            var face = BuildFace(theme, card);

            host.Children.Add(back);
            host.Children.Add(face);

            var cv = new CardVisual
            {
                Root = root,
                BackLayer = back,
                FaceLayer = face,
                FlipScale = flipScale,
                DealTranslate = dealTranslate
            };

            cv.SetFaceUp(startFaceUp);
            return cv;
        }

        private static Grid BuildBack(CardTheme theme)
        {
            var g = new Grid { Visibility = Visibility.Visible };

            g.Children.Add(new Border
            {
                Background = theme.BackBackground,
                BorderBrush = theme.BackBorder,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(theme.CornerRadius)
            });

            // simple back pattern
            g.Children.Add(new Rectangle
            {
                Margin = new Thickness(10),
                RadiusX = theme.CornerRadius - 4,
                RadiusY = theme.CornerRadius - 4,
                Stroke = new SolidColorBrush(Color.FromRgb(0xC7, 0xE7, 0xF2)),
                StrokeThickness = 2,
                Opacity = 0.6
            });

            g.Children.Add(new TextBlock
            {
                Text = "BLUES",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Black"),
                FontSize = 22,
                Opacity = 0.85,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            return g;
        }

        private static Grid BuildFace(CardTheme theme, Card c)
        {
            bool isRed = c.IsRed;
            Brush pip = isRed ? theme.PipRed : theme.PipBlack;

            var g = new Grid { Visibility = Visibility.Hidden };

            g.Children.Add(new Border
            {
                Background = theme.FaceBackground,
                BorderBrush = theme.FaceBorder,
                BorderThickness = new Thickness(3),
                CornerRadius = new CornerRadius(theme.CornerRadius)
            });

            // top-left label
            g.Children.Add(new TextBlock
            {
                Text = c.ShortText,
                Foreground = pip,
                FontFamily = new FontFamily("Segoe UI Black"),
                FontSize = 18,
                Margin = new Thickness(10, 8, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top
            });

            // big suit
            g.Children.Add(new TextBlock
            {
                Text = c.SuitText,
                Foreground = pip,
                FontSize = 64,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            // bottom-right rotated label
            var br = new TextBlock
            {
                Text = c.ShortText,
                Foreground = pip,
                FontFamily = new FontFamily("Segoe UI Black"),
                FontSize = 18,
                Margin = new Thickness(0, 0, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                RenderTransformOrigin = new Point(0.5, 0.5),
                RenderTransform = new RotateTransform(180)
            };
            g.Children.Add(br);

            return g;
        }

        public static Storyboard AnimateDealAndFlip(CardVisual v, CardTheme theme, bool flipToFace, int delayMs = 0)
        {
            // Deal: slide in from a small offset
            v.DealTranslate.X = -28;
            v.DealTranslate.Y = 14;

            var sb = new Storyboard();

            var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
            var easeInOut = new CubicEase { EasingMode = EasingMode.EaseInOut };

            var dealX = new DoubleAnimation(-28, 0, TimeSpan.FromMilliseconds(theme.DealMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(dealX, v.DealTranslate);
            Storyboard.SetTargetProperty(dealX, new PropertyPath(TranslateTransform.XProperty));

            var dealY = new DoubleAnimation(14, 0, TimeSpan.FromMilliseconds(theme.DealMs))
            {
                BeginTime = TimeSpan.FromMilliseconds(delayMs),
                EasingFunction = easeOut
            };
            Storyboard.SetTarget(dealY, v.DealTranslate);
            Storyboard.SetTargetProperty(dealY, new PropertyPath(TranslateTransform.YProperty));

            sb.Children.Add(dealX);
            sb.Children.Add(dealY);

            if (flipToFace)
            {
                // Flip = scale X to 0, swap layers, scale back to 1
                var tFlipStart = delayMs + theme.DealMs + 20;

                var shrink = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(theme.FlipMs / 2))
                {
                    BeginTime = TimeSpan.FromMilliseconds(tFlipStart),
                    EasingFunction = easeInOut
                };
                Storyboard.SetTarget(shrink, v.FlipScale);
                Storyboard.SetTargetProperty(shrink, new PropertyPath(ScaleTransform.ScaleXProperty));

                var grow = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(theme.FlipMs / 2))
                {
                    BeginTime = TimeSpan.FromMilliseconds(tFlipStart + theme.FlipMs / 2),
                    EasingFunction = easeInOut
                };
                Storyboard.SetTarget(grow, v.FlipScale);
                Storyboard.SetTargetProperty(grow, new PropertyPath(ScaleTransform.ScaleXProperty));

                sb.Children.Add(shrink);
                sb.Children.Add(grow);

                sb.CurrentTimeInvalidated += (_, __) =>
                {
                    // Swap exactly once when near midpoint
                    // (safe and cheap: only swap if currently face-down and we're at/after shrink end)
                    if (!v.IsFaceUp)
                    {
                        // crude but effective: when ScaleX is basically 0, swap
                        if (Math.Abs(v.FlipScale.ScaleX) < 0.05)
                            v.SetFaceUp(true);
                    }
                };
            }

            return sb;
        }
        public static FrameworkElement CreateCardView(Card c, CardTheme theme, bool faceDown, double w = 80, double h = 112)
        {
            var root = new Border
            {
                Width = w,
                Height = h,
                CornerRadius = new CornerRadius(theme.CornerRadius),
                BorderBrush = theme.CardBorder,
                BorderThickness = new Thickness(theme.BorderThickness),
                Background = faceDown ? theme.CardBack : theme.CardFace,
                SnapsToDevicePixels = true,
                Margin = new Thickness(6, 0, 6, 0)
            };

            if (faceDown)
            {
                root.Child = new TextBlock
                {
                    Text = "BB",
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Segoe UI Black"),
                    FontSize = 22,
                    Opacity = 0.9,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                return root;
            }

            string rank = RankText(c.Rank);
            string suit = SuitGlyph(c.Suit);
            var fg = c.IsRed ? theme.TextRed : theme.TextBlack;

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // top-left
            var tl = new TextBlock
            {
                Text = $"{rank}{suit}",
                Foreground = fg,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(10, 8, 0, 0)
            };
            Grid.SetRow(tl, 0);
            grid.Children.Add(tl);

            // center suit
            var mid = new TextBlock
            {
                Text = suit,
                Foreground = fg,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 44,
                Opacity = 0.9,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetRow(mid, 1);
            grid.Children.Add(mid);

            // bottom-right mirrored
            var br = new TextBlock
            {
                Text = $"{suit}{rank}",
                Foreground = fg,
                FontFamily = new FontFamily("Consolas"),
                FontWeight = FontWeights.Bold,
                FontSize = 16,
                Margin = new Thickness(0, 0, 10, 8),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid.SetRow(br, 2);
            grid.Children.Add(br);

            root.Child = grid;
            return root;
        }

        private static string RankText(Rank r) => r switch
        {
            Rank.Ace => "A",
            Rank.Jack => "J",
            Rank.Queen => "Q",
            Rank.King => "K",
            _ => ((int)r).ToString(CultureInfo.InvariantCulture)
        };

        private static string SuitGlyph(Suit s) => s switch
        {
            Suit.Clubs => "♣",
            Suit.Diamonds => "♦",
            Suit.Hearts => "♥",
            Suit.Spades => "♠",
            _ => "?"
        };
    }
}

