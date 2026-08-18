using System.Windows.Media;

namespace RehabEasy.App;

public sealed class ClinicalBarChartItem
{
    public string Label { get; init; } = string.Empty;
    public string ValueLabel { get; init; } = string.Empty;
    public double BarHeight { get; init; }
    public Brush Fill { get; init; } = Brushes.Teal;
    public string Tooltip { get; init; } = string.Empty;
}
