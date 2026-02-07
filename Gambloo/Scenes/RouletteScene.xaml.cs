using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using BluesBar.Gambloo;
using BluesBar.Systems;

namespace BluesBar.Gambloo.Scenes
{
    public partial class RouletteScene : UserControl, IGamblooScene
    {
        public string SceneId => "roulette";
        public string DisplayName => "Roulette";

        public bool IsBusy { get; private set; }
        public event Action<bool>? BusyChanged;

        private readonly Random _rng = new Random();

        // European wheel order (single zero)
        private static readonly int[] WheelOrder =
        {
            0,32,15,19,4,21,2,25,17,34,6,27,13,36,11,30,8,23,10,5,24,16,33,1,20,14,31,9,22,18,29,7,28,12,35,3,26
        };

        private static readonly HashSet<int> Reds = new HashSet<int>
        {
            1,3,5,7,9,12,14,16,18,19,21,23,25,27,30,32,34,36
        };

        // -------------------------
        // NEW: placed-chips betting (no slip list)
        // -------------------------
        private sealed class BetRegion
        {
            public string Key = "";
            public RouletteBet Prototype = new RouletteBet();
            public Rect HitRect;
            public Point Anchor;
            public string HoverLabel = "";
        }

        private readonly List<BetRegion> _regions = new();
        private readonly Dictionary<string, long> _placed = new();                // key -> amount
        private readonly Dictionary<string, Border> _chipVisuals = new();         // key -> chip UI
        private readonly Stack<(string key, long delta)> _undo = new();           // undo stack
        private Dictionary<string, long> _lastRoundPlaced = new();                // for Rebet

        // History (tiles UI)
        private readonly List<int> _history = new();
        const int MaxHistory = 60;

        // Wheel animation state
        private double _wheelAngle = 0; // degrees
        private readonly Dictionary<int, Shape> _wheelSegmentByNumber = new();

        // -------------------------
        // Table geometry (WIDE layout)
        // 3 rows x 12 cols:
        // row0: 1,4,7,...,34
        // row1: 2,5,8,...,35
        // row2: 3,6,9,...,36
        // -------------------------
        private Rect _rectGrid;
        private Rect _rectZero;
        private Rect _rectDozen1, _rectDozen2, _rectDozen3;
        private Rect _rectLow, _rectEven, _rectRed, _rectBlack, _rectOdd, _rectHigh;

        public RouletteScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                WireEventsOnce();
                BuildWheel();
                BuildTable();
                RefreshWalletHud();
                RefreshStakeHud();
                SeedFakeHistory(MaxHistory);
                RefreshHistoryTiles();
                ResizeHistoryGrid();
                ClearOutcome();
            };
        }

        private bool _wired = false;
        private void WireEventsOnce()
        {
            if (_wired) return;
            _wired = true;

            if (TableCanvas != null)
            {
                TableCanvas.SizeChanged += (_, __) => BuildTable();
                TableCanvas.MouseMove += TableCanvas_MouseMove;
                TableCanvas.MouseLeave += (_, __) => { if (HoverBetText != null) HoverBetText.Text = ""; };
            }

            if (WheelCanvas != null)
                WheelCanvas.SizeChanged += (_, __) => BuildWheel();

            // NEW: history grid columns adapt to available width
            if (HistoryPanel != null)
                HistoryPanel.SizeChanged += (_, __) => ResizeHistoryGrid();
            else if (HistoryTiles != null)
                HistoryTiles.SizeChanged += (_, __) => ResizeHistoryGrid();

            // also do one pass once layout exists
            Loaded += (_, __) => ResizeHistoryGrid();
        }

        public void OnShown()
        {
            RefreshWalletHud();
            RefreshStakeHud();
        }

        public void OnHidden() { }

        // -------------------------
        // Wallet HUD
        // -------------------------
        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            if (WalletText != null) WalletText.Text = $"Savings: {p.Coins:N0} Coins";
            if (NetWorthText != null) NetWorthText.Text = $"Net Worth: ${p.NetWorth:N0}";
        }

        // -------------------------
        // Colors
        // -------------------------
        private Brush BrushForNumber(int n)
        {
            if (n == 0) return new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x3B));
            return Reds.Contains(n)
                ? new SolidColorBrush(Color.FromRgb(0xE7, 0x4C, 0x3C))
                : new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11));
        }

        private string ColorName(int n)
        {
            if (n == 0) return "GREEN";
            return Reds.Contains(n) ? "RED" : "BLACK";
        }

        // -------------------------
        // Chip helpers
        // -------------------------
        private bool TryGetChip(out long chip)
        {
            chip = 0;
            if (ChipTextBox == null) return false;
            return long.TryParse(ChipTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out chip);
        }

        // Hooked from XAML: chip puck click (Border Tag="100"/"1000"/"5000" etc)
        private void ChipPreset_Click(object sender, MouseButtonEventArgs e)
        {
            if (IsBusy) return;
            if (sender is FrameworkElement fe && fe.Tag != null && ChipTextBox != null)
            {
                ChipTextBox.Text = fe.Tag.ToString() ?? "100";
            }
        }

        // -------------------------
        // Stake HUD
        // -------------------------
        private void RefreshStakeHud()
        {
            long stake = _placed.Values.Sum();
            if (TotalStakeText != null) TotalStakeText.Text = $"Stake: {stake:N0}";
        }

        private void ClearOutcome()
        {
            if (OutcomeText != null)
            {
                OutcomeText.Text = "";
                OutcomeText.Foreground = Brushes.White;
            }
        }

        private void SetOutcome(string msg, Brush color)
        {
            if (OutcomeText != null)
            {
                OutcomeText.Text = msg;
                OutcomeText.Foreground = color;
            }
        }

        // -------------------------
        // Wheel build + animation
        // -------------------------
        private void BuildWheel()
        {
            if (WheelCanvas == null || WheelRotate == null) return;

            WheelCanvas.Children.Clear();
            _wheelSegmentByNumber.Clear();

            double w = WheelCanvas.ActualWidth > 1 ? WheelCanvas.ActualWidth : WheelCanvas.Width;
            double h = WheelCanvas.ActualHeight > 1 ? WheelCanvas.ActualHeight : WheelCanvas.Height;

            if (w < 50 || h < 50) return;

            double cx = w / 2.0;
            double cy = h / 2.0;

            double outerR = Math.Min(w, h) * 0.48;
            double innerR = outerR * 0.62;

            // Outer ring
            var outer = new Ellipse
            {
                Width = outerR * 2,
                Height = outerR * 2,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Opacity = 0.85
            };
            Canvas.SetLeft(outer, cx - outerR);
            Canvas.SetTop(outer, cy - outerR);
            WheelCanvas.Children.Add(outer);

            int segCount = WheelOrder.Length;
            double step = 360.0 / segCount;

            for (int i = 0; i < segCount; i++)
            {
                int n = WheelOrder[i];

                double a0 = -90 + (i * step);
                double a1 = a0 + step;

                var wedge = MakeWedge(cx, cy, innerR, outerR, a0, a1, BrushForNumber(n));
                wedge.Opacity = 0.92;
                WheelCanvas.Children.Add(wedge);

                _wheelSegmentByNumber[n] = wedge;

                // label
                double mid = (a0 + a1) / 2.0;
                double rad = (innerR + outerR) / 2.0;
                var pt = Polar(cx, cy, rad, mid);

                var tb = new TextBlock
                {
                    Text = n.ToString(),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = Math.Max(11, outerR * 0.12),
                    FontWeight = FontWeights.SemiBold,
                    IsHitTestVisible = false
                };

                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var sz = tb.DesiredSize;

                Canvas.SetLeft(tb, pt.X - sz.Width / 2);
                Canvas.SetTop(tb, pt.Y - sz.Height / 2);
                WheelCanvas.Children.Add(tb);
            }

            // Inner circle
            var inner = new Ellipse
            {
                Width = innerR * 2,
                Height = innerR * 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Opacity = 0.9
            };
            Canvas.SetLeft(inner, cx - innerR);
            Canvas.SetTop(inner, cy - innerR);
            WheelCanvas.Children.Add(inner);

            WheelRotate.CenterX = cx;
            WheelRotate.CenterY = cy;
            WheelRotate.Angle = _wheelAngle;
        }

        private void ClearWheelHighlights()
        {
            foreach (var kv in _wheelSegmentByNumber)
            {
                kv.Value.Stroke = Brushes.Black;
                kv.Value.StrokeThickness = 1;
                kv.Value.Effect = null;
                kv.Value.Opacity = 0.92;
            }
        }

        private void HighlightWheelNumber(int n)
        {
            ClearWheelHighlights();

            if (_wheelSegmentByNumber.TryGetValue(n, out var seg))
            {
                seg.Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
                seg.StrokeThickness = 3;
                seg.Opacity = 1.0;

                seg.Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(0x6F, 0xC3, 0xE2),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }

            // optional ring pulse if present in XAML
            if (WheelLandingRing != null)
            {
                WheelLandingRing.Opacity = 0.0;
                var a = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180))
                {
                    AutoReverse = true,
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                WheelLandingRing.BeginAnimation(UIElement.OpacityProperty, a);
            }

            if (LandingText != null)
            {
                LandingText.Opacity = 1.0;
                var a = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200));
                LandingText.BeginAnimation(UIElement.OpacityProperty, a);
            }
        }

        private async Task AnimateWheelToResult(int result)
        {
            if (WheelRotate == null) return;

            int idx = Array.IndexOf(WheelOrder, result);
            if (idx < 0) idx = 0;

            double step = 360.0 / WheelOrder.Length;
            int spins = 6 + _rng.Next(0, 3); // 6-8 full spins

            // target: segment idx center to top (-90). Our segments are laid starting at -90, so:
            double target = (-idx * step) - (spins * 360.0);

            var tcs = new TaskCompletionSource<bool>();

            var anim = new DoubleAnimation
            {
                From = _wheelAngle,
                To = target,
                Duration = TimeSpan.FromMilliseconds(1800),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            anim.Completed += (_, __) =>
            {
                _wheelAngle = target % 360.0;
                WheelRotate.Angle = _wheelAngle;
                tcs.TrySetResult(true);
            };

            WheelRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
            await tcs.Task;
        }

        private static Path MakeWedge(double cx, double cy, double r0, double r1, double a0Deg, double a1Deg, Brush fill)
        {
            var p0 = Polar(cx, cy, r1, a0Deg);
            var p1 = Polar(cx, cy, r1, a1Deg);
            var p2 = Polar(cx, cy, r0, a1Deg);
            var p3 = Polar(cx, cy, r0, a0Deg);

            bool largeArc = (a1Deg - a0Deg) > 180;

            var fig = new PathFigure { StartPoint = p0, IsClosed = true, IsFilled = true };
            fig.Segments.Add(new ArcSegment(p1, new Size(r1, r1), 0, largeArc, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(p2, true));
            fig.Segments.Add(new ArcSegment(p3, new Size(r0, r0), 0, largeArc, SweepDirection.Counterclockwise, true));
            fig.Segments.Add(new LineSegment(p0, true));

            var geo = new PathGeometry();
            geo.Figures.Add(fig);

            return new Path
            {
                Data = geo,
                Fill = fill,
                Stroke = Brushes.Black,
                StrokeThickness = 1,
                Opacity = 1.0,
                IsHitTestVisible = false
            };
        }

        private static Point Polar(double cx, double cy, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        // -------------------------
        // Table build + betting (WIDE)
        // -------------------------
        private void BuildTable()
        {
            if (TableCanvas == null) return;

            double cw = TableCanvas.ActualWidth;
            double ch = TableCanvas.ActualHeight;
            if (cw < 80 || ch < 80) return;

            TableCanvas.Children.Clear();
            TableCanvas.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x2A, 0x18)); // felt

            _regions.Clear();

            if (ChipOverlayCanvas != null)
            {
                ChipOverlayCanvas.Children.Clear();
                ChipOverlayCanvas.Width = cw;
                ChipOverlayCanvas.Height = ch;
            }

            // Keep already-placed chips, just rebuild visuals at new positions
            _chipVisuals.Clear();

            // ----------------------------
            // Scale-to-fit geometry
            // ----------------------------
            const double baseCellW = 52;
            const double baseCellH = 44;
            const double basePad = 14;
            const double baseGap = 10;
            const double baseZeroW = 58;
            const double baseOutH = 42;
            const double baseDozH = 36;

            int cols = 12;
            int rows = 3;

            double baseGridW = baseCellW * cols;
            double baseGridH = baseCellH * rows;

            double baseTotalW = basePad + baseZeroW + baseGap + baseGridW + basePad;
            double baseTotalH = basePad + baseGridH + baseGap + baseOutH + baseGap + baseDozH + basePad;

            double scale = Math.Min(cw / baseTotalW, ch / baseTotalH);
            scale = Math.Max(0.65, Math.Min(1.55, scale));

            double CellW = baseCellW * scale;
            double CellH = baseCellH * scale;
            double Pad = basePad * scale;
            double Gap = baseGap * scale;
            double ZeroW = baseZeroW * scale;
            double OutH = baseOutH * scale;
            double DozH = baseDozH * scale;

            double HitPad = Math.Max(6, 10 * scale);

            double tableW = Pad + ZeroW + Gap + (CellW * cols) + Pad;
            double tableH = Pad + (CellH * rows) + Gap + OutH + Gap + DozH + Pad;

            double x0 = Math.Max(0, (cw - tableW) * 0.5);
            double y0 = Math.Max(0, (ch - tableH) * 0.5);

            _rectGrid = new Rect(x0 + Pad + ZeroW + Gap, y0 + Pad, CellW * cols, CellH * rows);
            _rectZero = new Rect(x0 + Pad, _rectGrid.Top, ZeroW, _rectGrid.Height);

            // Outside row (6)
            double outsideY = _rectGrid.Bottom + Gap;
            double outsideW = _rectGrid.Width / 6.0;

            _rectLow = new Rect(_rectGrid.Left + outsideW * 0, outsideY, outsideW, OutH);
            _rectEven = new Rect(_rectGrid.Left + outsideW * 1, outsideY, outsideW, OutH);
            _rectRed = new Rect(_rectGrid.Left + outsideW * 2, outsideY, outsideW, OutH);
            _rectBlack = new Rect(_rectGrid.Left + outsideW * 3, outsideY, outsideW, OutH);
            _rectOdd = new Rect(_rectGrid.Left + outsideW * 4, outsideY, outsideW, OutH);
            _rectHigh = new Rect(_rectGrid.Left + outsideW * 5, outsideY, outsideW, OutH);

            // Dozens row (3)
            double dozenY = outsideY + OutH + Gap;
            double dozenW = _rectGrid.Width / 3.0;

            _rectDozen1 = new Rect(_rectGrid.Left + dozenW * 0, dozenY, dozenW, DozH);
            _rectDozen2 = new Rect(_rectGrid.Left + dozenW * 1, dozenY, dozenW, DozH);
            _rectDozen3 = new Rect(_rectGrid.Left + dozenW * 2, dozenY, dozenW, DozH);

            // Outline
            var outline = new Rectangle
            {
                Width = _rectGrid.Width,
                Height = _rectGrid.Height,
                Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)),
                StrokeThickness = Math.Max(1.5, 2 * scale),
                Opacity = 0.65,
                RadiusX = 10,
                RadiusY = 10,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, _rectGrid.Left);
            Canvas.SetTop(outline, _rectGrid.Top);
            TableCanvas.Children.Add(outline);

            // Draw 0 (spans full height)
            DrawBox(_rectZero, "0", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x3B)), fontScale: 1.0);
            AddBetRegion(_rectZero.Expand(HitPad), AnchorCenter(_rectZero), RouletteBet.CreateStraight(0), "Straight 0");

            // Map numbers in wide layout
            int NumAt(int row, int col)
            {
                int baseN = 1 + col * 3;
                return baseN + row; // row 0 => base, 1 => base+1, 2 => base+2
            }

            // Number cells
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int n = NumAt(row, col);

                    Rect cell = new Rect(
                        _rectGrid.Left + col * CellW,
                        _rectGrid.Top + row * CellH,
                        CellW,
                        CellH);

                    DrawBox(cell, n.ToString(), Brushes.White, BrushForNumber(n), fontScale: 1.0);
                    AddBetRegion(cell.Expand(HitPad), AnchorCenter(cell), RouletteBet.CreateStraight(n), $"Straight {n}");
                }
            }

            // Splits: horizontal (adjacent cols, same row): +3
            for (int col = 0; col < cols - 1; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int a = NumAt(row, col);
                    int b = NumAt(row, col + 1);

                    double lx = _rectGrid.Left + (col + 1) * CellW;
                    double ty = _rectGrid.Top + row * CellH;

                    Rect band = new Rect(lx - (6 * scale), ty + (6 * scale), (12 * scale), CellH - (12 * scale));
                    var anchor = new Point(lx, ty + CellH * 0.5);
                    AddBetRegion(band.Expand(HitPad * 0.45), anchor, RouletteBet.CreateSplit(a, b), $"Split {a},{b}");
                }
            }

            // Splits: vertical (adjacent rows, same col): +1
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows - 1; row++)
                {
                    int a = NumAt(row, col);
                    int b = NumAt(row + 1, col);

                    double lx = _rectGrid.Left + col * CellW;
                    double ty = _rectGrid.Top + (row + 1) * CellH;

                    Rect band = new Rect(lx + (6 * scale), ty - (6 * scale), CellW - (12 * scale), (12 * scale));
                    var anchor = new Point(lx + CellW * 0.5, ty);
                    AddBetRegion(band.Expand(HitPad * 0.45), anchor, RouletteBet.CreateSplit(a, b), $"Split {a},{b}");
                }
            }

            // Corners
            for (int col = 0; col < cols - 1; col++)
            {
                for (int row = 0; row < rows - 1; row++)
                {
                    int n1 = NumAt(row, col);
                    int n2 = NumAt(row + 1, col);
                    int n3 = NumAt(row, col + 1);
                    int n4 = NumAt(row + 1, col + 1);

                    double ix = _rectGrid.Left + (col + 1) * CellW;
                    double iy = _rectGrid.Top + (row + 1) * CellH;

                    Rect hot = new Rect(ix - (8 * scale), iy - (8 * scale), (16 * scale), (16 * scale));
                    var anchor = new Point(ix, iy);
                    AddBetRegion(hot.Expand(HitPad * 0.35), anchor, RouletteBet.CreateCorner(new List<int> { n1, n2, n3, n4 }),
                        $"Corner {n1},{n2},{n3},{n4}");
                }
            }

            // Streets (each column = 3 numbers)
            for (int col = 0; col < cols; col++)
            {
                int a = NumAt(0, col);
                int b = NumAt(1, col);
                int c = NumAt(2, col);

                Rect band = new Rect(
                    _rectGrid.Left + col * CellW + (6 * scale),
                    _rectGrid.Bottom - (14 * scale),
                    CellW - (12 * scale),
                    (28 * scale));

                var anchor = new Point(_rectGrid.Left + col * CellW + CellW * 0.5, _rectGrid.Bottom - CellH * 0.15);
                AddBetRegion(band.Expand(HitPad * 0.30), anchor, RouletteBet.CreateStreet(new List<int> { a, b, c }), $"Street {a},{b},{c}");
            }

            // Six-lines (two adjacent streets = two columns)
            for (int col = 0; col < cols - 1; col++)
            {
                var nums = new List<int>
                {
                    NumAt(0,col), NumAt(1,col), NumAt(2,col),
                    NumAt(0,col+1), NumAt(1,col+1), NumAt(2,col+1)
                };

                Rect hot = new Rect(
                    _rectGrid.Left + (col + 1) * CellW - (10 * scale),
                    _rectGrid.Top + (CellH * 0.5) - (12 * scale),
                    (20 * scale),
                    (24 * scale));

                var anchor = new Point(_rectGrid.Left + (col + 1) * CellW, _rectGrid.Top + CellH * 0.5);
                AddBetRegion(hot.Expand(HitPad * 0.30), anchor, RouletteBet.CreateSixLine(nums), $"SixLine {string.Join(",", nums)}");
            }

            // Outside bets
            DrawBox(_rectLow, "1-18", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);
            DrawBox(_rectEven, "EVEN", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);
            DrawBox(_rectRed, "RED", Brushes.White, new SolidColorBrush(Color.FromRgb(0x6A, 0x12, 0x12)), fontScale: 0.95);
            DrawBox(_rectBlack, "BLACK", Brushes.White, new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)), fontScale: 0.95);
            DrawBox(_rectOdd, "ODD", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);
            DrawBox(_rectHigh, "19-36", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);

            AddBetRegion(_rectLow.Expand(HitPad), AnchorCenter(_rectLow), RouletteBet.CreateOutside("Low"), "Low");
            AddBetRegion(_rectEven.Expand(HitPad), AnchorCenter(_rectEven), RouletteBet.CreateOutside("Even"), "Even");
            AddBetRegion(_rectRed.Expand(HitPad), AnchorCenter(_rectRed), RouletteBet.CreateOutside("Red"), "Red");
            AddBetRegion(_rectBlack.Expand(HitPad), AnchorCenter(_rectBlack), RouletteBet.CreateOutside("Black"), "Black");
            AddBetRegion(_rectOdd.Expand(HitPad), AnchorCenter(_rectOdd), RouletteBet.CreateOutside("Odd"), "Odd");
            AddBetRegion(_rectHigh.Expand(HitPad), AnchorCenter(_rectHigh), RouletteBet.CreateOutside("High"), "High");

            // Dozens
            DrawBox(_rectDozen1, "1st 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);
            DrawBox(_rectDozen2, "2nd 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);
            DrawBox(_rectDozen3, "3rd 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)), fontScale: 0.95);

            AddBetRegion(_rectDozen1.Expand(HitPad), AnchorCenter(_rectDozen1), RouletteBet.CreateDozen(1), "Dozen 1");
            AddBetRegion(_rectDozen2.Expand(HitPad), AnchorCenter(_rectDozen2), RouletteBet.CreateDozen(2), "Dozen 2");
            AddBetRegion(_rectDozen3.Expand(HitPad), AnchorCenter(_rectDozen3), RouletteBet.CreateDozen(3), "Dozen 3");

            // Rebuild chip visuals at new anchors
            foreach (var r in _regions)
            {
                if (_placed.TryGetValue(r.Key, out var amt) && amt > 0)
                {
                    EnsureChipVisual(r);
                    UpdateChipVisual(r.Key);
                }
            }

            RefreshStakeHud();
        }

        private static Point AnchorCenter(Rect r) => new Point(r.Left + r.Width / 2.0, r.Top + r.Height / 2.0);

        private void DrawBox(Rect r, string label, Brush text, Brush fill, double fontScale)
        {
            var rect = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = fill,
                Stroke = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
                StrokeThickness = 1,
                RadiusX = 8,
                RadiusY = 8,
                Opacity = 0.95,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(rect, r.Left);
            Canvas.SetTop(rect, r.Top);
            TableCanvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = label,
                Foreground = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 13 * fontScale,
                FontWeight = FontWeights.SemiBold,
                IsHitTestVisible = false
            };

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;

            Canvas.SetLeft(tb, r.Left + (r.Width - sz.Width) / 2);
            Canvas.SetTop(tb, r.Top + (r.Height - sz.Height) / 2);
            TableCanvas.Children.Add(tb);
        }

        private void AddBetRegion(Rect hitRect, Point anchor, RouletteBet proto, string hover)
        {
            // Ensure stable key
            string key = proto.Key;

            var region = new BetRegion
            {
                Key = key,
                Prototype = proto,
                HitRect = hitRect,
                Anchor = anchor,
                HoverLabel = hover
            };
            _regions.Add(region);

            // Transparent clickable overlay
            var hit = new Rectangle
            {
                Width = hitRect.Width,
                Height = hitRect.Height,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = region
            };

            hit.MouseLeftButtonDown += (_, __) =>
            {
                if (IsBusy) return;

                if (!TryGetChip(out long chip) || chip <= 0)
                {
                    SetOutcome("Invalid chip amount.", Brushes.IndianRed);
                    return;
                }

                PlaceChip(region, chip);
            };

            Canvas.SetLeft(hit, hitRect.Left);
            Canvas.SetTop(hit, hitRect.Top);
            TableCanvas.Children.Add(hit);
        }

        private void TableCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (HoverBetText == null || TableCanvas == null) return;

            var p = e.GetPosition(TableCanvas);
            var r = HitTestRegion(p);
            HoverBetText.Text = r?.HoverLabel ?? "";
        }

        private BetRegion? HitTestRegion(Point p)
        {
            // Scan from last-added to first (later overlays “win”)
            for (int i = _regions.Count - 1; i >= 0; i--)
            {
                if (_regions[i].HitRect.Contains(p))
                    return _regions[i];
            }
            return null;
        }

        // -------------------------
        // Chip placement visuals
        // -------------------------
        private void PlaceChip(BetRegion r, long amount)
        {
            if (!_placed.ContainsKey(r.Key)) _placed[r.Key] = 0;
            _placed[r.Key] += amount;
            _undo.Push((r.Key, amount));

            EnsureChipVisual(r);
            UpdateChipVisual(r.Key);

            RefreshStakeHud();
            ClearOutcome();
        }

        private void EnsureChipVisual(BetRegion r)
        {
            if (ChipOverlayCanvas == null) return;
            if (_chipVisuals.ContainsKey(r.Key)) return;

            var puck = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x11, 0x11, 0x11)),
                BorderThickness = new Thickness(2),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.45
                },
                Child = new TextBlock
                {
                    Text = "",
                    Foreground = Brushes.Black,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                IsHitTestVisible = false
            };

            Canvas.SetLeft(puck, r.Anchor.X - 15);
            Canvas.SetTop(puck, r.Anchor.Y - 15);
            ChipOverlayCanvas.Children.Add(puck);

            _chipVisuals[r.Key] = puck;
        }

        private void UpdateChipVisual(string key)
        {
            if (!_chipVisuals.TryGetValue(key, out var puck)) return;
            if (puck.Child is not TextBlock t) return;

            long v = _placed.TryGetValue(key, out var amt) ? amt : 0;
            if (v <= 0)
            {
                // remove
                if (ChipOverlayCanvas != null)
                    ChipOverlayCanvas.Children.Remove(puck);

                _chipVisuals.Remove(key);
                return;
            }

            t.Text = v >= 1_000_000 ? $"{v / 1_000_000}M"
                 : v >= 1_000 ? $"{v / 1_000}k"
                 : v.ToString(CultureInfo.InvariantCulture);
        }

        private void ClearPlacedBets(bool clearUndo = true)
        {
            _placed.Clear();
            if (clearUndo) _undo.Clear();

            if (ChipOverlayCanvas != null)
                ChipOverlayCanvas.Children.Clear();

            _chipVisuals.Clear();
            RefreshStakeHud();
        }

        // -------------------------
        // Buttons (wired from XAML)
        // -------------------------
        private void RebetAndSpin_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            if (_lastRoundPlaced == null || _lastRoundPlaced.Count == 0)
            {
                SetOutcome("No previous bet to rebet.", Brushes.IndianRed);
                return;
            }

            _placed.Clear();
            foreach (var kv in _lastRoundPlaced)
                _placed[kv.Key] = kv.Value;

            RedrawPlacedChips();
            RefreshStakeHud();

            Spin_Click(SpinButton, new RoutedEventArgs());
        }
        private void ClearBets_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            ClearPlacedBets(clearUndo: true);
            SetOutcome("Cleared bets.", Brushes.White);
        }

        private void UndoBet_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            if (_undo.Count == 0) return;

            var (key, delta) = _undo.Pop();

            if (_placed.TryGetValue(key, out var cur))
            {
                cur -= delta;
                if (cur <= 0) _placed.Remove(key);
                else _placed[key] = cur;

                UpdateChipVisual(key);
                RefreshStakeHud();
                ClearOutcome();
            }
        }

        private void Rebet_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            if (_lastRoundPlaced.Count == 0)
            {
                SetOutcome("No last round bets to rebet.", Brushes.IndianRed);
                return;
            }

            ClearPlacedBets(clearUndo: true);

            foreach (var kv in _lastRoundPlaced)
                _placed[kv.Key] = kv.Value;

            // rebuild chip visuals from regions (anchors)
            foreach (var r in _regions)
            {
                if (_placed.TryGetValue(r.Key, out var amt) && amt > 0)
                {
                    EnsureChipVisual(r);
                    UpdateChipVisual(r.Key);
                }
            }

            RefreshStakeHud();
            SetOutcome("Rebet placed.", Brushes.White);
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            _history.Clear();
            RefreshHistoryTiles();
        }

        // -------------------------
        // Spin
        // -------------------------
        private void RedrawPlacedChips()
        {
            if (ChipOverlayCanvas == null) return;

            ChipOverlayCanvas.Children.Clear();

            // Draw in a stable order (regions order) so overlaps feel consistent
            foreach (var r in _regions)
            {
                if (string.IsNullOrWhiteSpace(r.Key)) continue;

                if (_placed.TryGetValue(r.Key, out var amt) && amt > 0)
                    DrawChipAt(r.Anchor, amt);
            }
        }

        private void DrawChipAt(Point anchor, long amount)
        {
            if (ChipOverlayCanvas == null) return;

            // Slightly larger for readability, still compact
            const double size = 28;

            var chip = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(size / 2),
                Background = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xD1, 0xD5, 0xDB)),
                BorderThickness = new Thickness(2),
                Child = new TextBlock
                {
                    Text = FormatChip(amount),
                    Foreground = Brushes.Black,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                },
                IsHitTestVisible = false
            };

            // Center on anchor
            Canvas.SetLeft(chip, anchor.X - size / 2);
            Canvas.SetTop(chip, anchor.Y - size / 2);

            ChipOverlayCanvas.Children.Add(chip);
        }

        private static string FormatChip(long amt)
        {
            if (amt >= 1_000_000) return (amt / 1_000_000d).ToString("0.#", CultureInfo.InvariantCulture) + "M";
            if (amt >= 1_000) return (amt / 1_000d).ToString("0.#", CultureInfo.InvariantCulture) + "K";
            return amt.ToString(CultureInfo.InvariantCulture);
        }


        private async void Spin_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            long stake = _placed.Values.Sum();
            if (stake <= 0)
            {
                SetOutcome("Place bets on the table first.", Brushes.IndianRed);
                return;
            }

            // Spend up front
            if (!ProfileManager.Instance.Spend(stake, "Roulette Stake"))
            {
                SetOutcome("Not enough coins.", Brushes.IndianRed);
                RefreshWalletHud();
                return;
            }
            _lastRoundPlaced = new Dictionary<string, long>(_placed);

            ClearWheelHighlights();
            if (LandingText != null) LandingText.Text = "--";

            RefreshWalletHud();
            SetBusy(true);

            int result = _rng.Next(0, 37);

            await AnimateWheelToResult(result);

            HighlightWheelNumber(result);

            if (LandingText != null)
                LandingText.Text = $"{result} {ColorName(result)}";

            if (ResultText != null) ResultText.Text = result.ToString(CultureInfo.InvariantCulture);
            if (ResultChip != null) ResultChip.Background = BrushForNumber(result);
            if (ResultColorText != null) ResultColorText.Text = ColorName(result);
            if (ResultColorChip != null) ResultColorChip.Background = BrushForNumber(result);

            // Resolve returns by iterating placed chips + matching regions/prototypes
            long totalReturn = 0;

            // Fast lookup from key -> prototype
            // (regions rebuilt on BuildTable; keys are stable)
            var protoByKey = _regions
                .GroupBy(r => r.Key)
                .ToDictionary(g => g.Key, g => g.First().Prototype);

            foreach (var kv in _placed)
            {
                if (kv.Value <= 0) continue;
                if (!protoByKey.TryGetValue(kv.Key, out var proto)) continue;

                var bet = proto.WithAmount(kv.Value);
                totalReturn += bet.ResolveReturn(result, Reds);
            }

            long profit = totalReturn - stake;

            if (totalReturn > 0)
                ProfileManager.Instance.Earn(totalReturn, "Roulette Payout");

            RefreshWalletHud();

            PushHistoryTile(result);

            SetOutcome(profit >= 0 ? $"+{profit:N0} profit" : $"{profit:N0} loss",
                       profit >= 0 ? Brushes.LightGreen : Brushes.IndianRed);

            // Casino-style: clear bets after spin
            ClearPlacedBets(clearUndo: true);

            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            if (SpinButton != null) SpinButton.IsEnabled = !busy;
            if (ChipTextBox != null) ChipTextBox.IsEnabled = !busy;
        }

        // -------------------------
        // History tiles (color squares with number text)
        // -------------------------
        private void ResizeHistoryGrid()
        {
            if (HistoryTiles == null) return;

            // UniformGrid doesn't always have width at first layout pass
            double w = HistoryTiles.ActualWidth;
            if (w <= 1) return;

            // Tile math: (tile width 44) + margin/padding (~8)
            double stride = 52;

            int cols = (int)Math.Floor(w / stride);
            cols = Math.Max(8, Math.Min(16, cols));   // clamp so it stays readable

            HistoryTiles.Columns = cols;
        }
        private void PushHistoryTile(int number)
        {
            _history.Insert(0, number);

            if (_history.Count > MaxHistory)
                _history.RemoveAt(_history.Count - 1);
            RefreshHistoryTiles();
        }

        private void RefreshHistoryTiles()
        {
            if (HistoryTiles == null) return;

            HistoryTiles.Children.Clear();

            for (int i = 0; i < _history.Count; i++)
            {
                int n = _history[i];

                var tile = new Border
                {
                    Width = 18,
                    Height = 18,
                    CornerRadius = new CornerRadius(3),
                    Background = BrushForNumber(n),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(0x22, 0x32, 0x4A)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(1),
                    Child = new TextBlock
                    {
                        Text = n.ToString(CultureInfo.InvariantCulture),
                        Foreground = Brushes.White,
                        FontFamily = new FontFamily("Consolas"),
                        FontSize = 9,
                        FontWeight = FontWeights.Bold,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                };

                // Most recent: louder border + slight scale + small glow
                if (i == 0)
                {
                    tile.BorderBrush = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2));
                    tile.BorderThickness = new Thickness(2);
                    tile.RenderTransformOrigin = new Point(0.5, 0.5);
                    tile.RenderTransform = new ScaleTransform(1.12, 1.12);
                    tile.Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(0x6F, 0xC3, 0xE2),
                        BlurRadius = 10,
                        ShadowDepth = 0,
                        Opacity = 0.55
                    };
                }

                HistoryTiles.Children.Add(tile);
            }
        }
        private void SeedFakeHistory(int count = 50)
        {
            // believable-ish distribution:
            // - greens: ~3-5% (European roulette is 1/37 ≈ 2.7%, but a tiny bit higher "to draw them in")
            // - otherwise red/black roughly balanced
            if (_history == null) return;

            _history.Clear();

            // Put a few recent-looking numbers first (optional)
            // Then fill the rest.
            for (int i = 0; i < count; i++)
            {
                int n = RollBelievableHistoryNumber();
                _history.Add(n);
            }

            RefreshHistoryTiles();
        }

        private int RollBelievableHistoryNumber()
        {
            // Greens: ~4%
            if (_rng.NextDouble() < 0.04)
                return 0;

            // Otherwise 1..36 uniformly
            return _rng.Next(1, 37);
        }


        // -------------------------
        // Models
        // -------------------------
        private sealed class RouletteBet
        {
            public string Kind { get; private set; } = "Straight";
            public long Amount { get; private set; } = 0;

            // Inside bets
            public List<int> Numbers { get; private set; } = new();

            // Dozen/Column selector 1..3
            public int Selector { get; private set; } = 0;

            // Stable dictionary key for placed-chip map (ignores Amount on purpose)
            public string Key
            {
                get
                {
                    return Kind switch
                    {
                        "Straight" => $"Straight:{Numbers[0]}",

                        "Split" =>
                            $"Split:{Math.Min(Numbers[0], Numbers[1])}-{Math.Max(Numbers[0], Numbers[1])}",

                        "Street" =>
                            $"Street:{string.Join("-", Numbers.OrderBy(x => x))}",

                        "Corner" =>
                            $"Corner:{string.Join("-", Numbers.OrderBy(x => x))}",

                        "SixLine" =>
                            $"SixLine:{string.Join("-", Numbers.OrderBy(x => x))}",

                        "Dozen" => $"Dozen:{Selector}",
                        "Column" => $"Column:{Selector}",

                        // outside (Red/Black/Odd/Even/Low/High/Green)
                        _ => Kind
                    };
                }
            }

            // ---- factories (amount = 0 by default, because "placed" dict stores the total)
            public static RouletteBet CreateOutside(string kind) =>
                new RouletteBet { Kind = kind, Amount = 0 };

            public static RouletteBet CreateDozen(int selector) =>
                new RouletteBet { Kind = "Dozen", Selector = selector, Amount = 0 };

            public static RouletteBet CreateColumn(int selector) =>
                new RouletteBet { Kind = "Column", Selector = selector, Amount = 0 };

            public static RouletteBet CreateStraight(int n) =>
                new RouletteBet { Kind = "Straight", Amount = 0, Numbers = new List<int> { n } };

            public static RouletteBet CreateSplit(int a, int b) =>
                new RouletteBet { Kind = "Split", Amount = 0, Numbers = new List<int> { a, b } };

            public static RouletteBet CreateStreet(List<int> ns) =>
                new RouletteBet { Kind = "Street", Amount = 0, Numbers = ns.ToList() };

            public static RouletteBet CreateCorner(List<int> ns) =>
                new RouletteBet { Kind = "Corner", Amount = 0, Numbers = ns.ToList() };

            public static RouletteBet CreateSixLine(List<int> ns) =>
                new RouletteBet { Kind = "SixLine", Amount = 0, Numbers = ns.ToList() };

            public RouletteBet WithAmount(long amt)
            {
                return new RouletteBet
                {
                    Kind = Kind,
                    Amount = amt,
                    Selector = Selector,
                    Numbers = Numbers.ToList()
                };
            }

            // total RETURN (stake+profit) if win else 0
            public long ResolveReturn(int result, HashSet<int> reds)
            {
                bool win = Kind switch
                {
                    "Straight" => Numbers.Count == 1 && result == Numbers[0],
                    "Split" => Numbers.Contains(result),
                    "Street" => Numbers.Contains(result),
                    "Corner" => Numbers.Contains(result),
                    "SixLine" => Numbers.Contains(result),

                    "Dozen" => Selector switch
                    {
                        1 => result >= 1 && result <= 12,
                        2 => result >= 13 && result <= 24,
                        3 => result >= 25 && result <= 36,
                        _ => false
                    },

                    "Column" => Selector switch
                    {
                        1 => result != 0 && result % 3 == 1,
                        2 => result != 0 && result % 3 == 2,
                        3 => result != 0 && result % 3 == 0,
                        _ => false
                    },

                    "Red" => result != 0 && reds.Contains(result),
                    "Black" => result != 0 && !reds.Contains(result),
                    "Odd" => result != 0 && (result % 2 == 1),
                    "Even" => result != 0 && (result % 2 == 0),
                    "High" => result >= 19 && result <= 36,
                    "Low" => result >= 1 && result <= 18,
                    "Green" => result == 0,

                    _ => false
                };

                if (!win) return 0;

                int profitOdds = Kind switch
                {
                    "Straight" => 35,
                    "Split" => 17,
                    "Street" => 11,
                    "Corner" => 8,
                    "SixLine" => 5,
                    "Dozen" => 2,
                    "Column" => 2,
                    "Green" => 35,

                    // even money
                    "Red" or "Black" or "Odd" or "Even" or "High" or "Low" => 1,

                    _ => 0
                };

                return Amount * (profitOdds + 1L);
            }
        }
    }

    internal static class UiExt
    {
        internal static Rect Expand(this Rect r, double pad)
            => new Rect(r.Left - pad, r.Top - pad, r.Width + pad * 2, r.Height + pad * 2);
    }
}


