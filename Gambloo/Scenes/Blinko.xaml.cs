using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
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

        // ----------------------------
        // Fixed board + buckets
        // ----------------------------
        private const int Rows = 16;             // FIXED
        private const int Buckets = Rows + 1;    // 17 buckets

        // Native board size (matches XAML PegLayerImage/BallLayer)
        private const double W = 780;
        private const double H = 392;

        // Board geometry
        private const double BoardTop = 10;
        private const double BoardLeft = 10;
        private const double BoardRight = W - 10;
        private const double BoardBottom = H - 10;

        private const double BucketTopY = H * 0.78;    // “floor” where buckets start
        private const double BucketBottomY = H - 12;   // where balls settle

        private const double PegRadius = 5.0;  // not tiny anymore
        private const double BallRadius = 5.0;

        private const double RailThickness = 3.0;
        private const double RailTopPad = 4.0;

        private readonly List<Point> _pegs = new();
        private readonly List<double> _railsX = new();

        // Single editable static array (clean + obvious)
        // 17 entries (Buckets = Rows + 1).
        // Edit this anytime.
        private static readonly double[] Multipliers =
        {
            1000, 200, 10, 6, 3, 1.2, 0.6, 0.2, 0.0, 0.2, 0.6, 1.2, 3, 6, 10, 200, 1000
        };

        // Physics: BAITED CHAOS
        private const double Gravity = 980.0;      // faster falls = more peg hits = more chaos
        private const double PegBounce = 0.62;     // bouncy, but not pinball
        private const double WallBounce = 0.28;    // edges feel alive when you touch them
        private const double TangentialKeep = 0.94;// preserves sideways motion (wiggles)
        private const double XDrag = 0.85;         // less damping => more drift
        private const double CenterPull = 3.69;    // gentle bias only
        private const double MaxDt = 1.0 / 30.0;

        // Add this near your other constants:
        private const double PegJitter = 140.0;    // “bait” wobble (px/s impulse scale)


        private double CenterX => (BoardLeft + BoardRight) * 0.5;

        // ----------------------------
        // Loop + auto
        // ----------------------------
        private bool _hooked;
        private DateTime _lastTickUtc;

        private readonly DispatcherTimer _autoTimer = new DispatcherTimer();

        private int _fpsFrames;
        private double _fpsAccum;

        private sealed class Ball
        {
            public long Stake;
            public Ellipse Shape = null!;
            public TranslateTransform TT = null!;
            public Point P;
            public Vector V;

            public bool Landed;
            public DateTime LandedUtc;
            public bool Fading;
            public int BucketIndex;
        }

        private readonly List<Ball> _balls = new();

        // Cache bucket UI cells for highlight
        private Border[] _bucketCells = Array.Empty<Border>();

        public BlinkoScene()
        {
            InitializeComponent();

            // Lock native sizes (so the Viewbox scaling is predictable)
            if (PegLayerImage != null)
            {
                PegLayerImage.Width = W;
                PegLayerImage.Height = H;
            }
            if (BallLayer != null)
            {
                BallLayer.Width = W;
                BallLayer.Height = H;
            }

            _autoTimer.Interval = TimeSpan.FromMilliseconds(100); // 10/sec
            _autoTimer.Tick += (_, __) => SpawnBallFromUi();

            Loaded += (_, __) =>
            {
                RefreshWalletHud();
                BuildBoardVisuals();
                BuildBucketRow();
                HookRender();
            };

            Unloaded += (_, __) =>
            {
                _autoTimer.Stop();
                UnhookRender();
            };
        }

        public void OnShown() => RefreshWalletHud();
        public void OnHidden() { }

        // ---------------- UI ----------------
        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            if (WalletText != null) WalletText.Text = $"Savings: {p.Coins:N0} Coins";
        }

        private string RiskMode =>
            (RiskCombo?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Medium";

        private bool TryGetBet(out long bet)
        {
            bet = 0;
            if (BetBox == null) return false;
            return long.TryParse(BetBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out bet);
        }

        private void SetLastWin(string msg, Brush? color = null)
        {
            if (LastWinText == null) return;
            LastWinText.Text = msg;
            LastWinText.Foreground = color ?? Brushes.Gold;
        }

        public void Plus1k_Click(object sender, RoutedEventArgs e) => AddToBet(1_000);

        public void Half_Click(object sender, RoutedEventArgs e)
        {
            if (BetBox == null) return;
            if (!TryGetBet(out var b)) b = 0;
            b = Math.Max(1, b / 2);
            BetBox.Text = b.ToString(CultureInfo.InvariantCulture);
        }

        public void Double_Click(object sender, RoutedEventArgs e)
        {
            if (BetBox == null) return;
            if (!TryGetBet(out var b)) b = 0;
            if (b > long.MaxValue / 2) b = long.MaxValue;
            else b = Math.Max(1, b * 2);
            BetBox.Text = b.ToString(CultureInfo.InvariantCulture);
        }

        private void AddToBet(long add)
        {
            if (BetBox == null) return;
            if (!TryGetBet(out var cur)) cur = 0;
            if (cur > long.MaxValue - add) cur = long.MaxValue;
            else cur += add;
            cur = Math.Max(1, cur);
            BetBox.Text = cur.ToString(CultureInfo.InvariantCulture);
        }

        private void Manual_Checked(object sender, RoutedEventArgs e)
        {
            if (AutoToggle != null) AutoToggle.IsChecked = false;
            _autoTimer.Stop();
        }

        private void Auto_Checked(object sender, RoutedEventArgs e)
        {
            if (ManualToggle != null) ManualToggle.IsChecked = false;
            _autoTimer.Start();
        }

        private void Auto_Unchecked(object sender, RoutedEventArgs e)
        {
            _autoTimer.Stop();
            if (ManualToggle != null) ManualToggle.IsChecked = true;
        }

        private void Risk_Changed(object sender, SelectionChangedEventArgs e)
        {
            // Risk only changes feel/spread, visuals same
        }

        private void Drop_Click(object sender, RoutedEventArgs e) => SpawnBallFromUi();

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            if (BallLayer != null)
            {
                foreach (var b in _balls.ToList())
                    BallLayer.Children.Remove(b.Shape);
            }
            _balls.Clear();
            SetLastWin("");
            ClearBucketHighlight();
        }

        private void SpawnBallFromUi()
        {
            if (!TryGetBet(out var stake) || stake <= 0)
            {
                SetLastWin("Invalid bet.", Brushes.IndianRed);
                return;
            }

            if (!ProfileManager.Instance.Spend(stake, "Blinko Bet"))
            {
                SetLastWin("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }

            RefreshWalletHud();
            SpawnBall(stake);
        }

        // ---------------- Board visuals (clean) ----------------
        private void BuildBoardVisuals()
        {
            BuildPegs();
            BuildRails();
            RenderPegLayer();
        }

        private void BuildPegs()
        {
            _pegs.Clear();

            // Make it fill width nicely.
            // Bottom row has 16 pegs => 15 gaps across usable width.
            double left = BoardLeft + 22;
            double right = BoardRight - 22;
            double usableW = right - left;

            double top = BoardTop + 18;
            double bottom = BucketTopY - 22;
            double usableH = bottom - top;

            double rowGap = usableH / (Rows - 1);
            double colGap = usableW / (Rows - 1);

            for (int r = 0; r < Rows; r++)
            {
                int cols = r + 1;
                double y = top + r * rowGap;

                double rowW = (cols - 1) * colGap;
                double startX = CenterX - rowW * 0.5;

                for (int c = 0; c < cols; c++)
                {
                    double x = startX + c * colGap;
                    _pegs.Add(new Point(x, y));
                }
            }
        }

        private void BuildRails()
        {
            _railsX.Clear();
            double bucketW = (BoardRight - BoardLeft) / Buckets;

            for (int i = 1; i < Buckets; i++)
            {
                _railsX.Add(BoardLeft + i * bucketW);
            }
        }

        private void RenderPegLayer()
        {
            if (PegLayerImage == null) return;

            int w = (int)W;
            int h = (int)H;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Frame
                var stroke = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)), 2);
                stroke.Freeze();

                dc.DrawRoundedRectangle(
                    new SolidColorBrush(Color.FromRgb(0x0B, 0x0F, 0x16)),
                    stroke,
                    new Rect(BoardLeft, BoardTop, BoardRight - BoardLeft, BoardBottom - BoardTop),
                    14, 14);

                // Pegs
                var pegBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
                pegBrush.Freeze();

                foreach (var p in _pegs)
                    dc.DrawEllipse(pegBrush, null, p, PegRadius, PegRadius);

                // Bucket floor line
                var railPen = new Pen(new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)), RailThickness);
                railPen.Freeze();

                dc.DrawLine(railPen, new Point(BoardLeft, BucketTopY), new Point(BoardRight, BucketTopY));

                // Bucket walls (bins)
                double railTop = BucketTopY + RailTopPad;
                double railBottom = BoardBottom;

                // left boundary
                dc.DrawLine(railPen, new Point(BoardLeft, railTop), new Point(BoardLeft, railBottom));
                // rails
                foreach (double x in _railsX)
                    dc.DrawLine(railPen, new Point(x, railTop), new Point(x, railBottom));
                // right boundary
                dc.DrawLine(railPen, new Point(BoardRight, railTop), new Point(BoardRight, railBottom));
            }

            var rtb = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            PegLayerImage.Source = rtb;
        }

        private void BuildBucketRow()
        {
            if (BucketGrid == null) return;

            BucketGrid.Rows = 1;
            BucketGrid.Columns = Buckets;
            BucketGrid.Children.Clear();

            _bucketCells = new Border[Buckets];

            for (int i = 0; i < Buckets; i++)
            {
                double m = Multipliers[i];

                Brush fg =
                    m >= 1000 ? Brushes.LightGreen :
                    m >= 10 ? new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)) :
                    m <= 0 ? Brushes.IndianRed :
                    Brushes.White;

                var txt = new TextBlock
                {
                    Text = m >= 1000 ? "1000x" : $"{m:0.##}x",
                    Foreground = fg,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    Opacity = 0.95
                };

                var cell = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    BorderThickness = new Thickness(1),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)),
                    Background = Brushes.Transparent,
                    Margin = new Thickness(2, 0, 2, 0),
                    Child = txt
                };

                _bucketCells[i] = cell;
                BucketGrid.Children.Add(cell);
            }
        }

        private void HighlightBucket(int idx, bool jackpot)
        {
            if (_bucketCells == null || _bucketCells.Length == 0) return;
            idx = Math.Clamp(idx, 0, _bucketCells.Length - 1);

            ClearBucketHighlight();

            var cell = _bucketCells[idx];
            cell.Background = jackpot
                ? new SolidColorBrush(Color.FromArgb(120, 120, 255, 210))
                : new SolidColorBrush(Color.FromArgb(80, 111, 195, 226));

            // fade back out
            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(750))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            a.Completed += (_, __) =>
            {
                cell.Background = Brushes.Transparent;
                cell.Opacity = 1;
            };

            cell.BeginAnimation(OpacityProperty, a);
        }

        private void ClearBucketHighlight()
        {
            if (_bucketCells == null) return;
            foreach (var c in _bucketCells)
            {
                if (c == null) continue;
                c.BeginAnimation(OpacityProperty, null);
                c.Opacity = 1;
                c.Background = Brushes.Transparent;
            }
        }

        // ---------------- Balls ----------------
        private void SpawnBall(long stake)
        {
            if (BallLayer == null) return;

            // Risk affects spread + initial vx, but physics keeps central
            double spread = RiskMode switch
            {
                "Low" => 14,
                "High" => 30,
                _ => 20
            };

            double x = CenterX + (_rng.NextDouble() * spread - spread * 0.5);
            double y = BoardTop + 18;

            var glow = new DropShadowEffect
            {
                Color = Colors.White,
                BlurRadius = 14,
                ShadowDepth = 0,
                Opacity = 0.90
            };

            var orb = new Ellipse
            {
                Width = BallRadius * 2,
                Height = BallRadius * 2,
                Fill = new SolidColorBrush(Color.FromRgb(110, 200, 255)),
                Effect = glow,
                Opacity = 0.98
            };

            var tt = new TranslateTransform(x - BallRadius, y - BallRadius);
            orb.RenderTransform = tt;

            BallLayer.Children.Add(orb);

            double vx = (_rng.NextDouble() * 160 - 80) * (RiskMode == "High" ? 1.0 : 0.65);

            _balls.Add(new Ball
            {
                Stake = stake,
                Shape = orb,
                TT = tt,
                P = new Point(x, y),
                V = new Vector(vx, 0),
                Landed = false
            });
        }

        private void ResolveLanding(Ball b)
        {
            double bucketW = (BoardRight - BoardLeft) / Buckets;

            int idx = (int)Math.Floor((b.P.X - BoardLeft) / bucketW);
            idx = Math.Clamp(idx, 0, Buckets - 1);
            b.BucketIndex = idx;

            double mult = Multipliers[idx];
            long payout = (long)Math.Round(b.Stake * mult);

            if (payout > 0)
                ProfileManager.Instance.Earn(payout, "Blinko Payout");

            RefreshWalletHud();

            long profit = payout - b.Stake;

            SetLastWin(
                profit >= 0 ? $"+{profit:N0}  ({mult:0.##}x)" : $"{profit:N0}  ({mult:0.##}x)",
                profit >= 0 ? Brushes.LightGreen : Brushes.IndianRed
            );

            bool jackpot = mult >= 1000;
            HighlightBucket(idx, jackpot);

            b.Landed = true;
            b.LandedUtc = DateTime.UtcNow;

            if (jackpot)
            {
                b.Shape.Fill = new SolidColorBrush(Color.FromRgb(160, 255, 210));
                JackpotFx();
            }
            else if (mult <= 0.0)
            {
                b.Shape.Fill = new SolidColorBrush(Color.FromRgb(255, 140, 140));
            }
        }

        private void FadeAndRemove(Ball b)
        {
            if (b.Fading) return;
            b.Fading = true;

            var a = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(420))
            {
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };

            a.Completed += (_, __) =>
            {
                if (BallLayer != null)
                    BallLayer.Children.Remove(b.Shape);

                _balls.Remove(b);
            };

            b.Shape.BeginAnimation(OpacityProperty, a);
        }

        private void JackpotFx()
        {
            if (BallLayer == null) return;

            var txt = new TextBlock
            {
                Text = "JACKPOT!",
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Segoe UI Black"),
                FontSize = 34,
                Effect = new DropShadowEffect { Color = Colors.White, BlurRadius = 25, ShadowDepth = 0, Opacity = 0.95 }
            };

            var s = new ScaleTransform(1, 1);
            txt.RenderTransform = s;
            txt.RenderTransformOrigin = new Point(0.5, 0.5);

            Canvas.SetLeft(txt, CenterX - 92);
            Canvas.SetTop(txt, BoardTop + 18);

            BallLayer.Children.Add(txt);

            var pop = new DoubleAnimation(1.0, 1.25, TimeSpan.FromMilliseconds(180))
            {
                AutoReverse = true,
                EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.6 }
            };
            s.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            s.BeginAnimation(ScaleTransform.ScaleYProperty, pop);

            var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(700))
            {
                BeginTime = TimeSpan.FromMilliseconds(420),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
            };
            fade.Completed += (_, __) => BallLayer.Children.Remove(txt);
            txt.BeginAnimation(OpacityProperty, fade);
        }

        // ---------------- Render loop ----------------
        private void HookRender()
        {
            if (_hooked) return;
            _hooked = true;
            _lastTickUtc = DateTime.UtcNow;
            CompositionTarget.Rendering += OnRender;
        }

        private void UnhookRender()
        {
            if (!_hooked) return;
            _hooked = false;
            CompositionTarget.Rendering -= OnRender;
        }

        private void OnRender(object? sender, EventArgs e)
        {
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
                double fps = _fpsFrames / _fpsAccum;
                _fpsFrames = 0;
                _fpsAccum = 0;

                if (FpsText != null) FpsText.Text = $"FPS: {fps:0}";
            }
        }

        private void Step(double dt)
        {
            if (_balls.Count == 0) return;

            double cx = CenterX;
            double leftWall = BoardLeft + BallRadius + 2;
            double rightWall = BoardRight - BallRadius - 2;

            for (int i = _balls.Count - 1; i >= 0; i--)
            {
                var b = _balls[i];

                if (b.Landed)
                {
                    if (!b.Fading && (DateTime.UtcNow - b.LandedUtc).TotalSeconds >= 2.0)
                        FadeAndRemove(b);
                    continue;
                }

                // gravity
                b.V = new Vector(b.V.X, b.V.Y + Gravity * dt);

                // horizontal damping
                double damp = Math.Exp(-XDrag * dt);
                b.V = new Vector(b.V.X * damp, b.V.Y);

                // gentle inward pull
                double dxToCenter = (cx - b.P.X);
                b.V = new Vector(b.V.X + (dxToCenter * CenterPull) * dt, b.V.Y);

                // integrate
                b.P = new Point(b.P.X + b.V.X * dt, b.P.Y + b.V.Y * dt);

                // walls
                if (b.P.X < leftWall)
                {
                    b.P = new Point(leftWall, b.P.Y);
                    b.V = new Vector(Math.Abs(b.V.X) * WallBounce + 100, b.V.Y);
                }
                else if (b.P.X > rightWall)
                {
                    b.P = new Point(rightWall, b.P.Y);
                    b.V = new Vector(-Math.Abs(b.V.X) * WallBounce - 100, b.V.Y);
                }

                // rails below bucket entrance: keep ball inside its slot visually
                if (b.P.Y >= BucketTopY + RailTopPad)
                {
                    foreach (double rx in _railsX)
                    {
                        double dx = b.P.X - rx;
                        double minDx = (RailThickness * 0.5) + BallRadius;

                        if (Math.Abs(dx) < minDx)
                        {
                            double sign = dx >= 0 ? 1 : -1;
                            b.P = new Point(rx + sign * minDx, b.P.Y);
                            b.V = new Vector(-b.V.X * 0.35, b.V.Y);
                        }
                    }
                }

                // peg collisions ONLY above bucket entrance
                if (b.P.Y < BucketTopY)
                {
                    for (int p = 0; p < _pegs.Count; p++)
                    {
                        var peg = _pegs[p];
                        double dx = b.P.X - peg.X;
                        double dy = b.P.Y - peg.Y;
                        double jitter = (_rng.NextDouble() * 2.0 - 1.0) * PegJitter;

                        double minD = BallRadius + PegRadius;
                        double d2 = dx * dx + dy * dy;
                        if (d2 >= minD * minD) continue;

                        double d = Math.Max(0.001, Math.Sqrt(d2));
                        double nx = dx / d;
                        double ny = dy / d;

                        // push out
                        b.P = new Point(peg.X + nx * minD, peg.Y + ny * minD);

                        // reflect about normal, keep tangential
                        double vn = b.V.X * nx + b.V.Y * ny;
                        var vN = new Vector(nx * vn, ny * vn);
                        var vT = b.V - vN;

                        b.V = (vT * TangentialKeep) - (vN * PegBounce);
                        b.V = new Vector(b.V.X + jitter, Math.Max(140, b.V.Y));
                    }
                }

                // bucket settling: drop into the bins, resolve at bottom
                if (b.P.Y + BallRadius >= BucketBottomY)
                {
                    b.P = new Point(b.P.X, BucketBottomY - BallRadius);
                    ResolveLanding(b);
                }

                // apply transform
                b.TT.X = b.P.X - BallRadius;
                b.TT.Y = b.P.Y - BallRadius;
            }
        }
    }
}



