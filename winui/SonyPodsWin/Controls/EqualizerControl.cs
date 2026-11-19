using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.UI;

namespace SonyPodsWin.Controls;

/// <summary>
/// 10 段交互式均衡器曲线控件（继承 Grid + 内部 Canvas，无需 Generic.xaml 模板查找）。
/// 逐项对齐 design-winui-v1.md 与 parity-checklist.md：
/// - 10 个可拖节点（也支持按设备能力切换 5 段或其他段数）
/// - Catmull-Rom 细线平滑曲线（#A9ADB3，1.5px，无渐变填充）
/// - 节点空闲时白描边深底，选中/拖动时香槟金（#D4B16A）描边与高亮
/// - 节点上方显示相对 center 的差值（+n / -n / 0），底部显示频点标签
/// - 拖动发 BandsChanged，松手发 BandsReleased 触发协议下发
/// </summary>
public sealed class EqualizerControl : Grid
{
    private static readonly string[] Labels10 = { "31", "63", "125", "250", "500", "1k", "2k", "4k", "8k", "16k" };
    private static readonly string[] Labels5 = { "400", "1k", "2.5k", "6.3k", "16k" };

    private static readonly SolidColorBrush GridLineBrush = new(Color.FromArgb(0xFF, 0x3A, 0x3D, 0x42));
    private static readonly SolidColorBrush CurveBrush = new(Color.FromArgb(0xFF, 0xA9, 0xAD, 0xB3));
    private static readonly SolidColorBrush NodeIdleFillBrush = new(Color.FromArgb(0xFF, 0x2A, 0x2D, 0x32));
    private static readonly SolidColorBrush NodeIdleStrokeBrush = new(Colors.White);
    private static readonly SolidColorBrush AccentGoldBrush = new(Color.FromArgb(0xFF, 0xD4, 0xB1, 0x6A));
    private static readonly SolidColorBrush LabelMutedBrush = new(Color.FromArgb(0xFF, 0xA9, 0xAD, 0xB3));
    private static readonly SolidColorBrush LabelValueBrush = new(Colors.White);

    private readonly Canvas _canvas = new()
    {
        Background = new SolidColorBrush(Colors.Transparent),
    };

    private int _rawMax = 12;
    private int _center = 6;
    private int[] _steps = Enumerable.Repeat(6, 10).ToArray();
    private int _selectedIndex = -1;
    private bool _isDragging;

    private const double LeftMargin = 38;
    private const double RightMargin = 20;
    private const double TopMargin = 26;
    private const double BottomMargin = 28;
    private const double NodeRadius = 6.5;

    public event EventHandler<IReadOnlyList<int>>? BandsChanged;
    public event EventHandler<IReadOnlyList<int>>? BandsReleased;

    public int BandCount => _steps.Length;
    public int RawMax => _rawMax;
    public int Center => _center;
    public IReadOnlyList<int> Steps => _steps;

    public EqualizerControl()
    {
        MinHeight = 200;
        HorizontalAlignment = HorizontalAlignment.Stretch;
        Background = new SolidColorBrush(Colors.Transparent);

        Children.Add(_canvas);

        SizeChanged += (_, _) => Redraw();
        PointerPressed += OnPointerPressed;
        PointerMoved += OnPointerMoved;
        PointerReleased += OnPointerReleased;
        PointerCanceled += OnPointerCanceled;
    }

    public void Configure(int bandCount, int rawMax, int center, IReadOnlyList<int>? initialSteps = null)
    {
        int count = Math.Max(1, bandCount);
        _rawMax = Math.Max(1, rawMax);
        _center = Math.Clamp(center, 0, _rawMax);

        if (initialSteps is not null && initialSteps.Count == count)
        {
            _steps = initialSteps.Select(v => Math.Clamp(v, 0, _rawMax)).ToArray();
        }
        else if (_steps.Length != count)
        {
            _steps = Enumerable.Repeat(_center, count).ToArray();
        }
        else
        {
            _steps = _steps.Select(v => Math.Clamp(v, 0, _rawMax)).ToArray();
        }

        if (_selectedIndex >= count)
        {
            _selectedIndex = -1;
        }

        Redraw();
    }

    public void SetSteps(IReadOnlyList<int> steps)
    {
        if (steps.Count == 0)
        {
            return;
        }

        _steps = steps.Select(v => Math.Clamp(v, 0, _rawMax)).ToArray();
        if (_selectedIndex >= _steps.Length)
        {
            _selectedIndex = -1;
        }

        Redraw();
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        PointerPoint pt = e.GetCurrentPoint(_canvas);
        if (!pt.Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isDragging = true;
        CapturePointer(e.Pointer);
        UpdateFromPoint(pt.Position, selectNearest: true);
        e.Handled = true;
    }

    private void OnPointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging || _selectedIndex < 0)
        {
            return;
        }

        PointerPoint pt = e.GetCurrentPoint(_canvas);
        UpdateFromPoint(pt.Position, selectNearest: false);
        e.Handled = true;
    }

    private void OnPointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleasePointerCapture(e.Pointer);
        Redraw();
        BandsReleased?.Invoke(this, _steps.ToArray());
        e.Handled = true;
    }

    private void OnPointerCanceled(object sender, PointerRoutedEventArgs e)
    {
        if (!_isDragging)
        {
            return;
        }

        _isDragging = false;
        ReleasePointerCapture(e.Pointer);
        Redraw();
    }

    private void UpdateFromPoint(Point pos, bool selectNearest)
    {
        double plotWidth = Math.Max(1, ActualWidth - LeftMargin - RightMargin);
        double plotHeight = Math.Max(1, ActualHeight - TopMargin - BottomMargin);
        int count = _steps.Length;

        if (selectNearest)
        {
            double bestDist = double.MaxValue;
            int bestIdx = 0;
            for (int i = 0; i < count; i++)
            {
                double nx = GetBandX(i, count, plotWidth);
                double dist = Math.Abs(pos.X - nx);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }

            _selectedIndex = bestIdx;
        }

        if (_selectedIndex < 0 || _selectedIndex >= count)
        {
            return;
        }

        double ratio = 1.0 - Math.Clamp((pos.Y - TopMargin) / plotHeight, 0.0, 1.0);
        int newStep = Math.Clamp((int)Math.Round(ratio * _rawMax), 0, _rawMax);
        if (_steps[_selectedIndex] != newStep)
        {
            _steps[_selectedIndex] = newStep;
            Redraw();
            BandsChanged?.Invoke(this, _steps.ToArray());
        }
        else
        {
            Redraw();
        }
    }

    private static double GetBandX(int index, int count, double plotWidth)
    {
        if (count <= 1)
        {
            return LeftMargin + plotWidth / 2.0;
        }

        return LeftMargin + (plotWidth * index / (count - 1));
    }

    private double GetStepY(int step, double plotHeight)
    {
        double ratio = _rawMax > 0 ? (double)step / _rawMax : 0.5;
        return TopMargin + plotHeight * (1.0 - ratio);
    }

    private string[] GetBandLabels(int count)
    {
        if (count == 10)
        {
            return Labels10;
        }

        if (count == 5)
        {
            return Labels5;
        }

        return Enumerable.Range(1, count).Select(i => $"B{i}").ToArray();
    }

    private void Redraw()
    {
        _canvas.Children.Clear();
        double width = ActualWidth;
        double height = ActualHeight;
        if (width < 80 || height < 80 || _steps.Length == 0)
        {
            return;
        }

        double plotWidth = Math.Max(1, width - LeftMargin - RightMargin);
        double plotHeight = Math.Max(1, height - TopMargin - BottomMargin);
        int count = _steps.Length;

        // 1. 水平参考线（+center / 0 / -center）与左侧刻度
        int[] ySteps = { _rawMax, _center, 0 };
        string[] yLabels = { $"+{_rawMax - _center}", "0", $"-{_center}" };
        for (int i = 0; i < ySteps.Length; i++)
        {
            double y = GetStepY(ySteps[i], plotHeight);
            var hLine = new Line
            {
                X1 = LeftMargin,
                Y1 = y,
                X2 = LeftMargin + plotWidth,
                Y2 = y,
                Stroke = GridLineBrush,
                StrokeThickness = i == 1 ? 1.2 : 0.8,
                Opacity = i == 1 ? 0.9 : 0.55,
            };
            _canvas.Children.Add(hLine);

            var axisText = new TextBlock
            {
                Text = yLabels[i],
                FontSize = 11,
                Foreground = LabelMutedBrush,
                Width = LeftMargin - 8,
                TextAlignment = TextAlignment.Right,
            };
            Canvas.SetLeft(axisText, 0);
            Canvas.SetTop(axisText, y - 8);
            _canvas.Children.Add(axisText);
        }

        // 2. 计算各节点坐标与垂直参考线
        var points = new Point[count];
        string[] labels = GetBandLabels(count);
        for (int i = 0; i < count; i++)
        {
            double x = GetBandX(i, count, plotWidth);
            double y = GetStepY(_steps[i], plotHeight);
            points[i] = new Point(x, y);

            var vLine = new Line
            {
                X1 = x,
                Y1 = TopMargin,
                X2 = x,
                Y2 = TopMargin + plotHeight,
                Stroke = GridLineBrush,
                StrokeThickness = 0.8,
                Opacity = 0.35,
            };
            _canvas.Children.Add(vLine);

            var freqText = new TextBlock
            {
                Text = labels[i],
                FontSize = 11,
                Foreground = LabelMutedBrush,
                TextAlignment = TextAlignment.Center,
                Width = 36,
            };
            Canvas.SetLeft(freqText, x - 18);
            Canvas.SetTop(freqText, TopMargin + plotHeight + 8);
            _canvas.Children.Add(freqText);
        }

        // 3. Catmull-Rom 平滑曲线（无渐变填充，1.5px 细线）
        if (count >= 2)
        {
            var curvePath = new Path
            {
                Stroke = CurveBrush,
                StrokeThickness = 1.5,
                StrokeLineJoin = PenLineJoin.Round,
                Data = BuildCatmullRomGeometry(points),
            };
            _canvas.Children.Add(curvePath);
        }

        // 4. 节点圆点与上方相对差值文字
        for (int i = 0; i < count; i++)
        {
            bool isSelected = i == _selectedIndex;
            double x = points[i].X;
            double y = points[i].Y;

            var node = new Ellipse
            {
                Width = NodeRadius * 2,
                Height = NodeRadius * 2,
                Fill = isSelected && _isDragging ? AccentGoldBrush : NodeIdleFillBrush,
                Stroke = isSelected ? AccentGoldBrush : NodeIdleStrokeBrush,
                StrokeThickness = isSelected ? 2.5 : 2.0,
            };
            Canvas.SetLeft(node, x - NodeRadius);
            Canvas.SetTop(node, y - NodeRadius);
            _canvas.Children.Add(node);

            int delta = _steps[i] - _center;
            string deltaText = delta > 0 ? $"+{delta}" : delta.ToString();
            var valueLabel = new TextBlock
            {
                Text = deltaText,
                FontSize = 11,
                FontWeight = isSelected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal,
                Foreground = isSelected ? AccentGoldBrush : LabelValueBrush,
                TextAlignment = TextAlignment.Center,
                Width = 32,
            };
            Canvas.SetLeft(valueLabel, x - 16);
            Canvas.SetTop(valueLabel, Math.Max(2, y - NodeRadius - 17));
            _canvas.Children.Add(valueLabel);
        }
    }

    private static PathGeometry BuildCatmullRomGeometry(Point[] pts)
    {
        var figure = new PathFigure { StartPoint = pts[0], IsClosed = false };
        var poly = new PolyLineSegment();
        const int segments = 12;

        for (int i = 0; i < pts.Length - 1; i++)
        {
            Point p0 = i > 0 ? pts[i - 1] : pts[i];
            Point p1 = pts[i];
            Point p2 = pts[i + 1];
            Point p3 = i + 2 < pts.Length ? pts[i + 2] : pts[i + 1];

            for (int s = 1; s <= segments; s++)
            {
                double t = (double)s / segments;
                double t2 = t * t;
                double t3 = t2 * t;

                double x = 0.5 * (
                    (2 * p1.X) +
                    (-p0.X + p2.X) * t +
                    (2 * p0.X - 5 * p1.X + 4 * p2.X - p3.X) * t2 +
                    (-p0.X + 3 * p1.X - 3 * p2.X + p3.X) * t3);

                double y = 0.5 * (
                    (2 * p1.Y) +
                    (-p0.Y + p2.Y) * t +
                    (2 * p0.Y - 5 * p1.Y + 4 * p2.Y - p3.Y) * t2 +
                    (-p0.Y + 3 * p1.Y - 3 * p2.Y + p3.Y) * t3);

                poly.Points.Add(new Point(x, y));
            }
        }

        figure.Segments.Add(poly);
        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        return geometry;
    }
}
