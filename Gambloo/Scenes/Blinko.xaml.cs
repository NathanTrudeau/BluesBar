using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class BlinkoScene : UserControl, IGamblooScene
    {
        public string SceneId => "blinko";
        public string DisplayName => "Blinko";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new();

        // perf + sim tuning
        private const double PegRadius = 5;
        private const double BallRadius = 8;
        private const double Gravity = 2200;   // px/s^2
        private const double Bounce = 0.88;
        private const double PegDeflect = 0.92;
        private const double MaxDt = 0.03;

        // dynamic layout
        private int _rows = 16;
        private double _boardW, _boardH;
        private double _bucketTopY;

        private readonly List<Point> _pegs = new();
        private double[] _multipliers = Array.Empty<double>();

        // rendering tick
        private bool _renderHooked;
        private DateTime _lastTickUtc;

        // fps counter
        private int _fpsFrames;
        private double _fpsAccum;
        private double _fps;

        private sealed class Ball
        {
            public Border Visual = new();
            public TranslateTransform TT = new();
            public Point P;
            public Vector V;
            public long Stake;
            public bool Landed;
            public DateTime LandedUtc;
            public int BucketIndex;
            public bool Fading;
        }

        private readonly List<Ball> _balls = new();

        public BlinkoScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                RefreshWalletHud();
                BuildAll();
                HookRender();
            };

            Unloaded += (_, __) => UnhookRender();

            if (BoardCanvas != null)
            {
                BoardCanvas.SizeChanged += (_, __) =>
                {
                    BuildAll();
                };
            }
        }

        public void OnShown()
        {
            RefreshWalletHud();
        }

        public void OnHidden() { }

        // ---------------------------
        // UI helpers
        // ---------------------------
        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            if (WalletText != null) WalletText.Text = $"Savings: {p.Coins:N0} Coins";
        }

        private void SetOutcome(string msg, Brush? color = null)
        {
            if (OutcomeText == null) return;
            OutcomeText.Text = msg;
            OutcomeText.Foreground = color ?? Brushes.White;
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            if (DropButton != null) DropButton.IsEnabled = !busy;
            if (BetTextBox != null) BetTextBox.IsEnabled = !busy;
            if (RiskCombo != null) RiskCombo.IsEnabled = !busy;
            if (RowsCombo != null) RowsCombo.IsEnabled = !busy;
        }

        private bool TryGetBet(out long bet)
        {
            bet = 0;
            return BetTextBox != null &&
                   long.TryParse(BetTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bet);
        }

        private void BetPlus1k_Click(object sender, RoutedEventArgs e) => AddToBet(1_000);
        private void Half_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBet(out var b)) b = 0;
            b = Math.Max(1, b / 2);
            BetTextBox.Text = b.ToString(CultureInfo.InvariantCulture);
        }

        private void Double_Click(object sender, RoutedEventArgs e)
        {
            if (!TryGetBet(out var b)) b = 0;
            b = Math.Max(1, b * 2);
            BetTextBox.Text = b.ToString(CultureInfo.InvariantCulture);
        }

        private void AddToBet(long add)
        {
            if (!TryGetBet(out var cur)) cur = 0;
            if (BetTextBox != null) BetTextBox.Text = (cur + add).ToString(CultureInfo.InvariantCulture);
        }

        private void RowsCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _rows = RowsCombo?.SelectedIndex switch
            {
                0 => 12,
                1 => 16,
                2 => 18,
                _ => 16
            };
            BuildAll();
        }

        // ---------------------------
        // Build UI / board
        // ---------------------------
        private void BuildAll()
        {
            if (BoardCanvas == null) return;

            _boardW = Math.Max(300, BoardCanvas.ActualWidth);
            _boardH = Math.Max(300, BoardCanvas.ActualHeight);
            _bucketTopY = _boardH * 0.84;

            BuildPegs();
            BuildMultipliers();
            BuildBucketRow();
            BuildRightList();
            RenderPegLayerToBitmap();
        }

        private void BuildPegs()
        {
            _pegs.Clear();

            // staggered rows in a pyramid
            double topPad = 28;
            double leftPad = 28;
            double rightPad = 28;

            int colsAtTop = 3;
            int colsAtBottom = colsAtTop + (_rows - 1);

            double usableW = _boardW - leftPad - rightPad;
            double gapX = usableW / (colsAtBottom - 1);
            double gapY = (_bucketTopY - topPad - 24) / (_rows - 1);

            for (int r = 0; r < _rows; r++)
            {
                int cols = colsAtTop + r;
                double rowW = (cols - 1) * gapX;
                double startX = (_boardW - rowW) * 0.5;
                double y = topPad + r * gapY;

                for (int c = 0; c < cols; c++)
                {
                    double x = startX + c * gapX;
                    _pegs.Add(new Point(x, y));
                }
            }
        }

        private void BuildMultipliers()
        {
            // buckets = bottom row pegs + 1
            // bottom row columns = colsAtTop + (rows - 1)
            int bottomCols = 3 + (_rows - 1);
            int buckets = bottomCols + 1;

            // Risk profile: wider tails + higher center for High risk
            string risk = (RiskCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";

            // start with symmetric "bell-ish" curve (lossy edges, spicy center)
            // then inject a hard center 1000x bucket.
            var baseArr = new double[buckets];
            int mid = buckets / 2;

            for (int i = 0; i < buckets; i++)
            {
                int d = Math.Abs(i - mid);

                // baseline curve
                double v = risk switch
                {
                    "Low" => Math.Max(0.5, 1.8 - d * 0.12),
                    "High" => Math.Max(0.2, 2.2 - d * 0.18),
                    _ => Math.Max(0.35, 2.0 - d * 0.15),
                };

                // edges should punish
                if (d > mid * 0.75) v *= 0.35;
                if (d > mid * 0.60) v *= 0.60;

                baseArr[i] = Math.Round(v, 2);
            }

            // Make center insane
            baseArr[mid] = 1000.0;

            // Make near-center juicy but not silly
            if (mid - 1 >= 0) baseArr[mid - 1] = risk == "High" ? 24.0 : 12.0;
            if (mid + 1 < buckets) baseArr[mid + 1] = risk == "High" ? 24.0 : 12.0;

            if (mid - 2 >= 0) baseArr[mid - 2] = risk == "High" ? 8.0 : 4.0;
            if (mid + 2 < buckets) baseArr[mid + 2] = risk == "High" ? 8.0 : 4.0;

            // Keep symmetric (we already are), store
            _multipliers = baseArr;
        }

        private void BuildBucketRow()
        {
            if (BucketGrid == null) return;
            BucketGrid.Columns = Math.Max(5, _multipliers.Length);
            BucketGrid.Children.Clear();

            for (int i = 0; i < _multipliers.Length; i++)
            {
                double m = _multipliers[i];

                Brush border = new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A));
                Brush bg = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20));
                Brush fg = Brushes.White;

                if (m >= 1000) { border = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)); fg = Brushes.LightGreen; }
                else if (m >= 10) { border = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)); fg = Brushes.LightGreen; }
                else if (m < 1) { fg = Brushes.IndianRed; }

                var tile = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    BorderBrush = border,
                    BorderThickness = new Thickness(2),
                    Background = bg,
                    Margin = new Thickness(4, 0, 4, 0),
                    Padding = new Thickness(6, 6, 6, 6),
                    Child = new TextBlock
                    {
                        Text = m >= 1000 ? "1000x" : $"x{m:0.##}",
                        Foreground = fg,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                BucketGrid.Children.Add(tile);
            }
        }

        private void BuildRightList()
        {
            if (RightMultiplierStack == null) return;
            RightMultiplierStack.Children.Clear();

            for (int i = 0; i < _multipliers.Length; i++)
            {
                double m = _multipliers[i];
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(999),
                    BorderThickness = new Thickness(2),
                    BorderBrush = m >= 10 ? new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)) : new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)),
                    Background = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20)),
                    Padding = new Thickness(8, 6, 8, 6),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = new TextBlock
                    {
                        Text = m >= 1000 ? "1000x" : $"x{m:0.##}",
                        Foreground = (m < 1) ? Brushes.IndianRed : (m >= 10 ? Brushes.LightGreen : Brushes.White),
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 12,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                };
                RightMultiplierStack.Children.Add(pill);
            }
        }

        private void RenderPegLayerToBitmap()
        {
            if (PegLayerImage == null) return;

            int w = Math.Max(1, (int)Math.Round(_boardW));
            int h = Math.Max(1, (int)Math.Round(_bucketTopY));

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // border frame
                var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)), 2);
                dc.DrawRectangle(null, stroke, new Rect(0, 0, _boardW, _bucketTopY));

                // pegs
                var pegBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
                pegBrush.Freeze();
                foreach (var p in _pegs)
                    dc.DrawEllipse(pegBrush, null, p, PegRadius, PegRadius);

                // bucket separators
                int buckets = _multipliers.Length;
                double bucketW = _boardW / buckets;
                var sepPen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)), 2) { DashStyle = DashStyles.Solid };
                sepPen.Freeze();

                for (int i = 1; i < buckets; i++)
                {
                    double x = i * bucketW;
                    dc.DrawLine(sepPen, new Point(x, _bucketTopY), new Point(x, _boardH));
                }

                // floor
                dc.DrawLine(stroke, new Point(0, _bucketTopY), new Point(_boardW, _bucketTopY));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PegLayerImage.Source = rtb;

            // size peg image to match canvas
            PegLayerImage.Width = _boardW;
            PegLayerImage.Height = _bucketTopY;
            Canvas.SetLeft(PegLayerImage, 0);
            Canvas.SetTop(PegLayerImage, 0);
        }

        // ---------------------------
        // Rendering loop (smooth)
        // ---------------------------
        private void HookRender()
        {
            if (_renderHooked) return;
            _renderHooked = true;
            _lastTickUtc = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRender;
        }

        private void UnhookRender()
        {
            if (!_renderHooked) return;
            _renderHooked = false;
            CompositionTarget.Rendering -= OnRender;
        }

        private void OnRender(object? sender, EventArgs e)
        {
            if (_balls.Count == 0)
            {
                // still update fps display slowly
                UpdateFps(0.016);
                return;
            }

            var now = DateTime.UtcNow;
            double dt = (now - _lastTickUtc).TotalSeconds;
            _lastTickUtc = now;
            dt = Math.Clamp(dt, 0.001, MaxDt);

            Step(dt);
            UpdateFps(dt);
        }

        private void UpdateFps(double dt)
        {
            _fpsFrames++;
            _fpsAccum += dt;

            if (_fpsAccum >= 0.5)
            {
                _fps = _fpsFrames / _fpsAccum;
                _fpsFrames = 0;
                _fpsAccum = 0;

                if (FpsText != null)
                    FpsText.Text = $"FPS: {_fps:0}";
            }
        }

        // ---------------------------
        // Gameplay
        // ---------------------------
        private void Drop_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            if (!TryGetBet(out var stake) || stake <= 0)
            {
                SetOutcome("Invalid bet.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(stake, "Blinko Stake"))
            {
                SetOutcome("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            RefreshWalletHud();
            SpawnBall(stake);

            SetOutcome($"Dropped {stake:N0}.", Brushes.White);
        }

        private void SpawnBall(long stake)
        {
            if (BallLayer == null) return;

            double x = _boardW * 0.5 + (_rng.NextDouble() * 20 - 10);
            double y = 18;

            // ball visual = Border + text, moved with TranslateTransform (no layout)
            var ballBorder = new Border
            {
                Width = BallRadius * 2,
                Height = BallRadius * 2,
                CornerRadius = new CornerRadius(999),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)),
                BorderThickness = new Thickness(2),
                Opacity = 0.98,
                RenderTransform = new TranslateTransform(x - BallRadius, y - BallRadius),
                Child = new TextBlock
                {
                    Text = "•",
                    Foreground = new SolidColorBrush(Color.FromRgb(0x0B, 0x12, 0x20)),
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            BallLayer.Children.Add(ballBorder);

            var tt = (TranslateTransform)ballBorder.RenderTransform;

            var b = new Ball
            {
                Stake = stake,
                Visual = ballBorder,
                TT = tt,
                P = new Point(x, y),
                V = new Vector(_rng.NextDouble() * 120 - 60, 0),
                Landed = false
            };

            _balls.Add(b);
        }

        private void Step(double dt)
        {
            for (int i = _balls.Count - 1; i >= 0; i--)
            {
                var b = _balls[i];

                if (b.Landed)
                {
                    // clear after couple seconds
                    var alive = (DateTime.UtcNow - b.LandedUtc).TotalSeconds;
                    if (!b.Fading && alive >= 1.6)
                    {
                        b.Fading = true;
                        FadeAndRemove(b);
                    }
                    continue;
                }

                // gravity
                b.V = new Vector(b.V.X, b.V.Y + Gravity * dt);

                // integrate
                b.P = new Point(b.P.X + b.V.X * dt, b.P.Y + b.V.Y * dt);

                // walls
                if (b.P.X - BallRadius < 0)
                {
                    b.P = new Point(BallRadius, b.P.Y);
                    b.V = new Vector(-b.V.X * Bounce, b.V.Y);
                }
                if (b.P.X + BallRadius > _boardW)
                {
                    b.P = new Point(_boardW - BallRadius, b.P.Y);
                    b.V = new Vector(-b.V.X * Bounce, b.V.Y);
                }

                // peg collisions (simple circle collision)
                for (int p = 0; p < _pegs.Count; p++)
                {
                    var peg = _pegs[p];
                    double dx = b.P.X - peg.X;
                    double dy = b.P.Y - peg.Y;
                    double dist2 = dx * dx + dy * dy;

                    double minDist = BallRadius + PegRadius;
                    if (dist2 < minDist * minDist)
                    {
                        double dist = Math.Max(0.001, Math.Sqrt(dist2));
                        double nx = dx / dist;
                        double ny = dy / dist;

                        // push out
                        b.P = new Point(peg.X + nx * minDist, peg.Y + ny * minDist);

                        // decompose velocity
                        double vn = b.V.X * nx + b.V.Y * ny;
                        var vN = new Vector(nx * vn, ny * vn);
                        var vT = b.V - vN;

                        // reflect normal, keep tangential
                        b.V = (vT * PegDeflect) - (vN * Bounce);

                        // tiny chaos
                        b.V = new Vector(b.V.X + (_rng.NextDouble() * 30 - 15), b.V.Y);
                    }
                }

                // landing
                if (b.P.Y + BallRadius >= _bucketTopY)
                {
                    b.P = new Point(b.P.X, _bucketTopY - BallRadius);
                    b.Landed = true;
                    b.LandedUtc = DateTime.UtcNow;

                    ResolveLanding(b);
                }

                // update transform (no layout)
                b.TT.X = b.P.X - BallRadius;
                b.TT.Y = b.P.Y - BallRadius;
            }
        }

        private void ResolveLanding(Ball b)
        {
            int buckets = _multipliers.Length;
            double bucketW = _boardW / buckets;

            int idx = (int)Math.Floor(b.P.X / bucketW);
            idx = Math.Clamp(idx, 0, buckets - 1);
            b.BucketIndex = idx;

            double mult = _multipliers[idx];
            long payout = (long)Math.Round(b.Stake * mult);

            if (payout > 0)
                ProfileManager.Instance.Earn(payout, "Blinko Payout");

            RefreshWalletHud();

            long profit = payout - b.Stake;

            if (BoardHintText != null)
                BoardHintText.Text = $"Bucket {idx + 1}/{buckets}  x{mult:0.##}";

            if (mult >= 1000)
            {
                // make the ball “legendary”
                b.Visual.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
                b.Visual.BorderThickness = new Thickness(3);
            }
            else if (mult >= 10)
            {
                b.Visual.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
            }
            else if (mult < 1)
            {
                b.Visual.BorderBrush = new SolidColorBrush(Color.FromRgb(0xB9, 0x1C, 0x1C));
            }

            SetOutcome(
                profit >= 0
                    ? $"+{profit:N0} profit (Paid {payout:N0})"
                    : $"{profit:N0} loss (Paid {payout:N0})",
                profit >= 0 ? Brushes.LightGreen : Brushes.IndianRed
            );
        }

        private void FadeAndRemove(Ball b)
        {
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            a.Completed += (_, __) =>
            {
                if (BallLayer != null)
                    BallLayer.Children.Remove(b.Visual);

                _balls.Remove(b);
            };

            b.Visual.BeginAnimation(OpacityProperty, a);
        }
    }
}
