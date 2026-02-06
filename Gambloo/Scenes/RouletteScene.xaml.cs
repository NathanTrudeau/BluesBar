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

        private readonly List<RouletteBet> _slip = new();
        private readonly List<SpinResult> _history = new();

        // Wheel animation state
        private double _wheelAngle = 0; // degrees

        // Table geometry (European layout)
        // 1..36 in 12 rows x 3 cols:
        // bottom row: 1,2,3; top row: 34,35,36 (standard felt)
        private const double FeltPad = 10;

        // Smaller visuals
        private const double CellW = 54;
        private const double CellH = 38;
        private const double ZeroW = 54;
        private const double ZeroH = 38;

        // Bigger invisible click padding
        private const double HitPad = 8;

        // Derived
        private Rect _rectGrid;   // main 12x3 area
        private Rect _rectZero;   // 0 box
        private Rect _rectDozen1, _rectDozen2, _rectDozen3;
        private Rect _rectLow, _rectEven, _rectRed, _rectBlack, _rectOdd, _rectHigh;
        private Rect _rectCol1, _rectCol2, _rectCol3; // right-side columns bets

        private readonly Dictionary<int, Shape> _wheelSegmentByNumber = new();

        public RouletteScene()
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                BuildWheel();
                BuildTable();
                RefreshWalletHud();
                RefreshSlipUi();
                RefreshHistoryUi();
            };

            TableCanvas.SizeChanged += (_, __) => BuildTable();
        }

        public void OnShown()
        {
            RefreshWalletHud();
        }

        public void OnHidden() { }

        // -------------------------
        // Wallet HUD
        // -------------------------
        private void RefreshWalletHud()
        {
            var p = ProfileManager.Instance.Current;
            WalletText.Text = $"Savings: {p.Coins:N0} Coins";
            NetWorthText.Text = $"Net Worth: ${p.NetWorth:N0}";
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
        // Slip / History UI
        // -------------------------
        private void RefreshSlipUi()
        {
            SlipListBox.Items.Clear();
            foreach (var b in _slip)
                SlipListBox.Items.Add(b.ToDisplayString());

            SlipCountText.Text = $"{_slip.Count} bets";

            long stake = _slip.Sum(x => x.Amount);
            TotalStakeText.Text = $"Total Stake: {stake:N0}";

            long maxPayout = _slip.Count == 0 ? 0 : _slip.Max(x => x.GetReturnIfWin());
            PotentialText.Text = _slip.Count == 0 ? "Potential: -" : $"Potential: up to {maxPayout:N0} return (one bet hit)";
        }

        private void PushHistory(int number)
        {
            _history.Insert(0, new SpinResult(number, ColorName(number)));
            if (_history.Count > 10) _history.RemoveAt(_history.Count - 1);
            RefreshHistoryUi();
        }

        private void RefreshHistoryUi()
        {
            HistoryList.Items.Clear();
            foreach (var h in _history)
                HistoryList.Items.Add(h.ToDisplayString());
        }

        // -------------------------
        // Chip helpers
        // -------------------------
        private bool TryGetChip(out long chip)
        {
            chip = 0;
            return long.TryParse(ChipTextBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out chip);
        }

        private void ChipPlus1k_Click(object sender, RoutedEventArgs e) => AddToChip(1_000);
        private void ChipPlus10k_Click(object sender, RoutedEventArgs e) => AddToChip(10_000);

        private void AddToChip(long add)
        {
            if (!TryGetChip(out var cur)) cur = 0;
            ChipTextBox.Text = (cur + add).ToString(CultureInfo.InvariantCulture);
        }

        // -------------------------
        // Wheel build + animation
        // -------------------------
        private void BuildWheel()
        {
            if (WheelCanvas == null) return;

            WheelCanvas.Children.Clear();
            _wheelSegmentByNumber.Clear();

            double w = WheelCanvas.Width;
            double h = WheelCanvas.Height;
            double cx = w / 2.0;
            double cy = h / 2.0;

            double outerR = Math.Min(w, h) * 0.48;
            double innerR = outerR * 0.62;

            // Outer ring
            WheelCanvas.Children.Add(new Ellipse
            {
                Width = outerR * 2,
                Height = outerR * 2,
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Opacity = 0.85
            }.WithPos(cx - outerR, cy - outerR));

            // Segment wedges + labels
            int segCount = WheelOrder.Length;
            double step = 360.0 / segCount;

            for (int i = 0; i < segCount; i++)
            {
                int n = WheelOrder[i];

                // Segment center angle: start at -90 (top), go clockwise
                double a0 = -90 + (i * step);
                double a1 = a0 + step;

                var wedge = MakeWedge(cx, cy, innerR, outerR, a0, a1, BrushForNumber(n));
                wedge.Opacity = 0.92;
                WheelCanvas.Children.Add(wedge);

                // Keep reference for highlight (last one wins if duplicates, but numbers are unique)
                _wheelSegmentByNumber[n] = wedge;

                // Label
                double mid = (a0 + a1) / 2.0;
                double rad = (innerR + outerR) / 2.0;
                var pt = Polar(cx, cy, rad, mid);

                var tb = new TextBlock
                {
                    Text = n.ToString(),
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                };

                // Center label on point
                tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                var sz = tb.DesiredSize;

                Canvas.SetLeft(tb, pt.X - sz.Width / 2);
                Canvas.SetTop(tb, pt.Y - sz.Height / 2);

                WheelCanvas.Children.Add(tb);
            }

            // Inner circle
            WheelCanvas.Children.Add(new Ellipse
            {
                Width = innerR * 2,
                Height = innerR * 2,
                Fill = new SolidColorBrush(Color.FromRgb(0x18, 0x18, 0x18)),
                Stroke = Brushes.White,
                StrokeThickness = 2,
                Opacity = 0.9
            }.WithPos(cx - innerR, cy - innerR));

            // Reset transform angle
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
                seg.Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)); // Accent2-ish
                seg.StrokeThickness = 3;
                seg.Opacity = 1.0;

                seg.Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Color.FromRgb(0x6F, 0xC3, 0xE2),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.9
                };
            }
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
                Opacity = 1.0
            };
        }

        private static Point Polar(double cx, double cy, double r, double deg)
        {
            double rad = deg * Math.PI / 180.0;
            return new Point(cx + r * Math.Cos(rad), cy + r * Math.Sin(rad));
        }

        private async Task AnimateWheelToResult(int result)
        {
            int idx = Array.IndexOf(WheelOrder, result);
            if (idx < 0) idx = 0;

            // Want segment center at top (-90). Since our segment centers already start at -90,
            // rotate wheel by -idx*step (plus extra spins).
            double step = 360.0 / WheelOrder.Length;

            int spins = 6 + _rng.Next(0, 3); // 6-8 full spins
            double target = (-idx * step) - (spins * 360.0);

            // Animate from current to target
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

        // -------------------------
        // Table build + betting
        // -------------------------

        private void TableCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            // Prevent spam redraw while layout is still settling
            if (TableCanvas.ActualWidth < 50 || TableCanvas.ActualHeight < 50) return;

            BuildTable(); // <-- your method that draws the felt + regions
        }
        private void BuildTable()
        {
            if (TableCanvas == null) return;

            double cw = TableCanvas.ActualWidth;
            double ch = TableCanvas.ActualHeight;
            if (cw < 50 || ch < 50) return;

            TableCanvas.Children.Clear();
            TableCanvas.Background = new SolidColorBrush(Color.FromRgb(0x0F, 0x2A, 0x18)); // felt

            // ----------------------------
            // WIDE grid: 3 rows x 12 cols
            // ----------------------------
            const double baseCellW = 52;
            const double baseCellH = 44;
            const double basePad = 14;
            const double baseGap = 10;
            const double baseZeroW = 58;     // 0 box width
            const double baseOutH = 42;     // outside bet height
            const double baseDozH = 36;     // dozens height

            int cols = 12;
            int rows = 3;

            double baseGridW = baseCellW * cols;
            double baseGridH = baseCellH * rows;

            // total footprint:
            // pad + (zero box) + gap + (grid) + pad
            // height: pad + grid + gap + outside + gap + dozens + pad
            double baseTotalW = basePad + baseZeroW + baseGap + baseGridW + basePad;
            double baseTotalH = basePad + baseGridH + baseGap + baseOutH + baseGap + baseDozH + basePad;

            double scale = Math.Min(cw / baseTotalW, ch / baseTotalH);
            scale = Math.Max(0.65, Math.Min(1.45, scale));

            double CellW = baseCellW * scale;
            double CellH = baseCellH * scale;
            double Pad = basePad * scale;
            double Gap = baseGap * scale;
            double ZeroW = baseZeroW * scale;
            double OutH = baseOutH * scale;
            double DozH = baseDozH * scale;

            double HitPadLocal = Math.Max(6, HitPad * scale);

            // Center the whole layout
            double tableW = Pad + ZeroW + Gap + (CellW * cols) + Pad;
            double tableH = Pad + (CellH * rows) + Gap + OutH + Gap + DozH + Pad;

            double x0 = Math.Max(0, (cw - tableW) * 0.5);
            double y0 = Math.Max(0, (ch - tableH) * 0.5);

            // Main grid rect
            _rectGrid = new Rect(x0 + Pad + ZeroW + Gap, y0 + Pad, CellW * cols, CellH * rows);

            // Zero rect (spans full grid height)
            _rectZero = new Rect(x0 + Pad, _rectGrid.Top, ZeroW, _rectGrid.Height);

            // Outside bets row (6 across under grid)
            double outsideY = _rectGrid.Bottom + Gap;
            double outsideW = _rectGrid.Width / 6.0;

            _rectLow = new Rect(_rectGrid.Left + outsideW * 0, outsideY, outsideW, OutH);
            _rectEven = new Rect(_rectGrid.Left + outsideW * 1, outsideY, outsideW, OutH);
            _rectRed = new Rect(_rectGrid.Left + outsideW * 2, outsideY, outsideW, OutH);
            _rectBlack = new Rect(_rectGrid.Left + outsideW * 3, outsideY, outsideW, OutH);
            _rectOdd = new Rect(_rectGrid.Left + outsideW * 4, outsideY, outsideW, OutH);
            _rectHigh = new Rect(_rectGrid.Left + outsideW * 5, outsideY, outsideW, OutH);

            // Dozens row (3 across)
            double dozenY = outsideY + OutH + Gap;
            double dozenW = _rectGrid.Width / 3.0;

            _rectDozen1 = new Rect(_rectGrid.Left + dozenW * 0, dozenY, dozenW, DozH);
            _rectDozen2 = new Rect(_rectGrid.Left + dozenW * 1, dozenY, dozenW, DozH);
            _rectDozen3 = new Rect(_rectGrid.Left + dozenW * 2, dozenY, dozenW, DozH);

            // ----------------------------
            // Draw 0
            // ----------------------------
            DrawBox(_rectZero, "0", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1F, 0x7A, 0x3B)));
            AddBetRegion(ExpandRect(_rectZero, HitPadLocal), RouletteBet.CreateStraight(0, 0), "Straight 0");

            // Helpful outline around grid
            var outline = new Rectangle
            {
                Width = _rectGrid.Width,
                Height = _rectGrid.Height,
                Stroke = new SolidColorBrush(Color.FromRgb(0x6F, 0xC3, 0xE2)),
                StrokeThickness = Math.Max(1.5, 2 * scale),
                Opacity = 0.8,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(outline, _rectGrid.Left);
            Canvas.SetTop(outline, _rectGrid.Top);
            TableCanvas.Children.Add(outline);

            // ----------------------------
            // Numbers 1..36 in WIDE layout:
            // row0: 1,4,7,...,34
            // row1: 2,5,8,...,35
            // row2: 3,6,9,...,36
            // ----------------------------
            int NumAt(int row, int col)
            {
                // col 0 => base=1, col 1 => base=4 ... base = 1 + col*3
                int baseN = 1 + col * 3;
                return baseN + row; // row 0 => base, row1 => base+1, row2 => base+2
            }

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

                    DrawBox(cell, n.ToString(), Brushes.White, (Brush)BrushForNumber(n));
                    AddBetRegion(ExpandRect(cell, HitPadLocal), RouletteBet.CreateStraight(n, 0), $"Straight {n}");
                }
            }

            // ----------------------------
            // SPLITS
            // 1) Horizontal splits (adjacent columns, same row): +3
            // ----------------------------
            for (int col = 0; col < cols - 1; col++)
            {
                for (int row = 0; row < rows; row++)
                {
                    int a = NumAt(row, col);
                    int b = NumAt(row, col + 1); // +3

                    double lx = _rectGrid.Left + (col + 1) * CellW;
                    double ty = _rectGrid.Top + row * CellH;

                    Rect band = new Rect(lx - (6 * scale), ty + (6 * scale), (12 * scale), CellH - (12 * scale));
                    AddBetRegion(ExpandRect(band, HitPadLocal * 0.45), RouletteBet.CreateSplit(a, b, 0), $"Split {a},{b}");
                }
            }

            // 2) Vertical splits (adjacent rows, same column): +1
            for (int col = 0; col < cols; col++)
            {
                for (int row = 0; row < rows - 1; row++)
                {
                    int a = NumAt(row, col);
                    int b = NumAt(row + 1, col); // +1

                    double lx = _rectGrid.Left + col * CellW;
                    double ty = _rectGrid.Top + (row + 1) * CellH;

                    Rect band = new Rect(lx + (6 * scale), ty - (6 * scale), CellW - (12 * scale), (12 * scale));
                    AddBetRegion(ExpandRect(band, HitPadLocal * 0.45), RouletteBet.CreateSplit(a, b, 0), $"Split {a},{b}");
                }
            }

            // ----------------------------
            // CORNERS
            // corners at intersections of 2 rows x 2 cols
            // {n, n+1, n+3, n+4} mapping holds with this layout.
            // ----------------------------
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
                    AddBetRegion(ExpandRect(hot, HitPadLocal * 0.35),
                        RouletteBet.CreateCorner(new List<int> { n1, n2, n3, n4 }, 0),
                        $"Corner {n1},{n2},{n3},{n4}");
                }
            }

            // ----------------------------
            // STREETS (3 numbers): each column is a street (base, base+1, base+2)
            // put a small band at the bottom of each column (still clickable)
            // ----------------------------
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

                AddBetRegion(ExpandRect(band, HitPadLocal * 0.30),
                    RouletteBet.CreateStreet(new List<int> { a, b, c }, 0),
                    $"Street {a},{b},{c}");
            }

            // ----------------------------
            // SIX-LINES (two adjacent streets): columns col and col+1 (6 nums)
            // ----------------------------
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

                AddBetRegion(ExpandRect(hot, HitPadLocal * 0.30),
                    RouletteBet.CreateSixLine(nums, 0),
                    $"SixLine {string.Join(",", nums)}");
            }

            // ----------------------------
            // Outside bets
            // ----------------------------
            DrawBox(_rectLow, "1-18", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            DrawBox(_rectEven, "EVEN", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            DrawBox(_rectRed, "RED", Brushes.White, new SolidColorBrush(Color.FromRgb(0x6A, 0x12, 0x12)));
            DrawBox(_rectBlack, "BLACK", Brushes.White, new SolidColorBrush(Color.FromRgb(0x10, 0x10, 0x10)));
            DrawBox(_rectOdd, "ODD", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            DrawBox(_rectHigh, "19-36", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));

            AddBetRegion(ExpandRect(_rectLow, HitPadLocal), RouletteBet.CreateOutside("Low", 0), "Low");
            AddBetRegion(ExpandRect(_rectEven, HitPadLocal), RouletteBet.CreateOutside("Even", 0), "Even");
            AddBetRegion(ExpandRect(_rectRed, HitPadLocal), RouletteBet.CreateOutside("Red", 0), "Red");
            AddBetRegion(ExpandRect(_rectBlack, HitPadLocal), RouletteBet.CreateOutside("Black", 0), "Black");
            AddBetRegion(ExpandRect(_rectOdd, HitPadLocal), RouletteBet.CreateOutside("Odd", 0), "Odd");
            AddBetRegion(ExpandRect(_rectHigh, HitPadLocal), RouletteBet.CreateOutside("High", 0), "High");

            // ----------------------------
            // Dozens (based on numeric ranges)
            // 1-12 = first 4 columns, 13-24 = next 4, 25-36 = last 4
            // ----------------------------
            DrawBox(_rectDozen1, "1st 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            DrawBox(_rectDozen2, "2nd 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));
            DrawBox(_rectDozen3, "3rd 12", Brushes.White, new SolidColorBrush(Color.FromRgb(0x1A, 0x1A, 0x1A)));

            AddBetRegion(ExpandRect(_rectDozen1, HitPadLocal), RouletteBet.CreateDozenOrColumn("Dozen", 1, 0), "Dozen 1");
            AddBetRegion(ExpandRect(_rectDozen2, HitPadLocal), RouletteBet.CreateDozenOrColumn("Dozen", 2, 0), "Dozen 2");
            AddBetRegion(ExpandRect(_rectDozen3, HitPadLocal), RouletteBet.CreateDozenOrColumn("Dozen", 3, 0), "Dozen 3");
        }



        private static Rect ExpandRect(Rect r, double pad)
            => new Rect(r.X - pad, r.Y - pad, r.Width + pad * 2, r.Height + pad * 2);

        private static Rect ExpandRect(Rect r, double padX, double padY)
            => new Rect(r.X - padX, r.Y - padY, r.Width + padX * 2, r.Height + padY * 2);


        private void DrawBox(Rect r, string label, Brush text, Brush fill)
        {
            var rect = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = fill,
                Stroke = new SolidColorBrush(Color.FromRgb(0x70, 0x70, 0x70)),
                StrokeThickness = 1,
                RadiusX = 6,
                RadiusY = 6,
                Opacity = 0.95
            };
            Canvas.SetLeft(rect, r.Left);
            Canvas.SetTop(rect, r.Top);
            TableCanvas.Children.Add(rect);

            var tb = new TextBlock
            {
                Text = label,
                Foreground = text,
                FontFamily = new FontFamily("Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold
            };

            tb.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            var sz = tb.DesiredSize;

            Canvas.SetLeft(tb, r.Left + (r.Width - sz.Width) / 2);
            Canvas.SetTop(tb, r.Top + (r.Height - sz.Height) / 2);
            TableCanvas.Children.Add(tb);
        }

        private void AddBetRegion(Rect r, RouletteBet betTemplate, string debugName)
        {
            // Transparent clickable rectangle overlay
            var hit = new Rectangle
            {
                Width = r.Width,
                Height = r.Height,
                Fill = Brushes.Transparent,
                Cursor = Cursors.Hand,
                Tag = betTemplate
            };

            hit.MouseLeftButtonDown += (_, __) =>
            {
                if (IsBusy) return;

                if (!TryGetChip(out long chip) || chip <= 0)
                {
                    OutcomeText.Text = "Invalid chip amount.";
                    OutcomeText.Foreground = Brushes.IndianRed;
                    return;
                }

                var b = (RouletteBet)hit.Tag!;
                var actual = b.WithAmount(chip);

                _slip.Add(actual);
                RefreshSlipUi();

                OutcomeText.Text = $"Added {actual.Kind} {actual.Brief()}  x {chip:N0}";
                OutcomeText.Foreground = Brushes.White;
            };

            Canvas.SetLeft(hit, r.Left);
            Canvas.SetTop(hit, r.Top);
            TableCanvas.Children.Add(hit);
        }

        // -------------------------
        // Buttons
        // -------------------------
        private void ClearSlip_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;
            _slip.Clear();
            RefreshSlipUi();
            OutcomeText.Text = "Cleared slip.";
            OutcomeText.Foreground = Brushes.White;
        }

        private void ClearHistory_Click(object sender, RoutedEventArgs e)
        {
            _history.Clear();
            RefreshHistoryUi();
        }

        // -------------------------
        // Spin
        // -------------------------
        private async void Spin_Click(object sender, RoutedEventArgs e)
        {
            if (IsBusy) return;

            if (_slip.Count == 0)
            {
                OutcomeText.Text = "Place bets on the table first.";
                OutcomeText.Foreground = Brushes.IndianRed;
                return;
            }

            long stake = _slip.Sum(x => x.Amount);
            if (stake <= 0)
            {
                OutcomeText.Text = "Invalid stake.";
                OutcomeText.Foreground = Brushes.IndianRed;
                return;
            }

            // Spend up front
            if (!ProfileManager.Instance.Spend(stake, "Roulette Stake"))
            {
                OutcomeText.Text = "Not enough coins.";
                OutcomeText.Foreground = Brushes.IndianRed;
                RefreshWalletHud();
                return;
            }

            ClearWheelHighlights();
            if (LandingText != null) LandingText.Text = "--";

            RefreshWalletHud();
            SetBusy(true);

            // Choose result (0..36)
            int result = _rng.Next(0, 37);

            // Animate wheel to that result (visual sync)
            await AnimateWheelToResult(result);

            HighlightWheelNumber(result);

            // Landing label (new XAML name)
            if (LandingText != null)
                LandingText.Text = $"{result} {ColorName(result)}";

            // Display result
            ResultText.Text = result.ToString();
            ResultChip.Background = BrushForNumber(result);
            ResultColorText.Text = ColorName(result);
            ResultColorChip.Background = BrushForNumber(result);

            // Resolve returns
            long totalReturn = 0;
            foreach (var bet in _slip)
                totalReturn += bet.ResolveReturn(result, Reds);

            long profit = totalReturn - stake;

            if (totalReturn > 0)
                ProfileManager.Instance.Earn(totalReturn, "Roulette Payout");

            RefreshWalletHud();
            PushHistory(result);

            OutcomeText.Text = profit >= 0 ? $"+{profit:N0} profit" : $"{profit:N0} loss";
            OutcomeText.Foreground = profit >= 0 ? Brushes.LightGreen : Brushes.IndianRed;

            // Clear slip after spin
            _slip.Clear();
            RefreshSlipUi();

            SetBusy(false);
        }

        private void SetBusy(bool busy)
        {
            IsBusy = busy;
            BusyChanged?.Invoke(busy);

            SpinButton.IsEnabled = !busy;
            ChipTextBox.IsEnabled = !busy;
        }

        // -------------------------
        // Models
        // -------------------------
        private sealed class RouletteBet
        {
            public string Kind { get; private set; } = "Straight";
            public long Amount { get; private set; }
            public List<int> Numbers { get; private set; } = new(); // inside bets
            public int Selector { get; private set; } = 0;          // dozen/column 1-3

            public static RouletteBet CreateOutside(string kind, long amt) =>
                new RouletteBet { Kind = kind, Amount = amt };

            public static RouletteBet CreateDozenOrColumn(string kind, int selector, long amt) =>
                new RouletteBet { Kind = kind, Selector = selector, Amount = amt };

            public static RouletteBet CreateStraight(int n, long amt) =>
                new RouletteBet { Kind = "Straight", Amount = amt, Numbers = new List<int> { n } };

            public static RouletteBet CreateSplit(int a, int b, long amt) =>
                new RouletteBet { Kind = "Split", Amount = amt, Numbers = new List<int> { a, b } };

            public static RouletteBet CreateStreet(List<int> ns, long amt) =>
                new RouletteBet { Kind = "Street", Amount = amt, Numbers = ns.ToList() };

            public static RouletteBet CreateCorner(List<int> ns, long amt) =>
                new RouletteBet { Kind = "Corner", Amount = amt, Numbers = ns.ToList() };

            public static RouletteBet CreateSixLine(List<int> ns, long amt) =>
                new RouletteBet { Kind = "SixLine", Amount = amt, Numbers = ns.ToList() };

            public RouletteBet WithAmount(long amt)
            {
                return new RouletteBet
                {
                    Kind = Kind,
                    Amount = amt,
                    Numbers = Numbers.ToList(),
                    Selector = Selector
                };
            }

            public string Brief()
            {
                return Kind switch
                {
                    "Straight" or "Split" or "Street" or "Corner" or "SixLine" => $"[{string.Join(",", Numbers)}]",
                    "Dozen" => $"[D{Selector}]",
                    "Column" => $"[C{Selector}]",
                    _ => ""
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

                    // 0 only (European)
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

            public long GetReturnIfWin()
            {
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
                    "Red" or "Black" or "Odd" or "Even" or "High" or "Low" => 1,
                    _ => 0
                };

                return Amount * (profitOdds + 1L);
            }

            public string ToDisplayString()
            {
                string sel = Brief();
                return $"{Kind,-8} {sel,-14} x {Amount:N0}";
            }
        }

        private readonly record struct SpinResult(int Number, string ColorName)
        {
            public string ToDisplayString() => $"{Number,2}  {ColorName}";
        }


    }


    internal static class UiExt
    {
        public static T WithPos<T>(this T el, double x, double y) where T : FrameworkElement
        {
            Canvas.SetLeft(el, x);
            Canvas.SetTop(el, y);
            return el;
        }
        internal static Rect Expand(this Rect r, double pad)
        {
            return new Rect(r.Left - pad, r.Top - pad, r.Width + pad * 2, r.Height + pad * 2);
        }
    }
}

