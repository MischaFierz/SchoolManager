using System.Windows;
using System.Windows.Controls;

namespace SchoolManager.App;

/// <summary>
/// Ordnet Kalenderkarten nach der Uhrzeit an: senkrecht nach Beginn und Dauer,
/// waagrecht nebeneinander, sobald sich Einträge zeitlich überschneiden. Wie
/// viele Karten nebeneinander stehen, rechnet die Kalenderseite aus und gibt es
/// über die angehängten Eigenschaften Column und ColumnCount mit.
/// </summary>
public sealed class TimeGridPanel : Panel
{
    /// <summary>Beginn der Achse in Minuten ab Mitternacht.</summary>
    public static readonly DependencyProperty DayStartProperty =
        DependencyProperty.Register(nameof(DayStart), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(7d * 60, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Ende der Achse in Minuten ab Mitternacht.</summary>
    public static readonly DependencyProperty DayEndProperty =
        DependencyProperty.Register(nameof(DayEnd), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(18d * 60, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Wie viele Bildpunkte eine Minute hoch ist.</summary>
    public static readonly DependencyProperty MinuteHeightProperty =
        DependencyProperty.Register(nameof(MinuteHeight), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(1.2d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>Abstand zwischen zwei nebeneinander liegenden Karten.</summary>
    public static readonly DependencyProperty GapProperty =
        DependencyProperty.Register(nameof(Gap), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(4d, FrameworkPropertyMetadataOptions.AffectsArrange));

    /// <summary>So hoch ist eine Karte mindestens, damit sie lesbar bleibt.</summary>
    public static readonly DependencyProperty MinimumHeightProperty =
        DependencyProperty.Register(nameof(MinimumHeight), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(26d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// So breit sollte eine Karte mindestens sein. Reicht die Breite für alle
    /// gleichzeitigen Karten nebeneinander nicht mehr aus, werden sie stattdessen
    /// gestaffelt übereinandergelegt.
    /// </summary>
    public static readonly DependencyProperty MinimumWidthProperty =
        DependencyProperty.Register(nameof(MinimumWidth), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(86d, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>
    /// Um so viel ragt beim Staffeln jede Karte links unter der nächsten
    /// hervor. Dieser Streifen ist alles, was von einer verdeckten Karte übrig
    /// bleibt - er muss breit genug sein, dass man ihn sieht und mit der Maus
    /// trifft.
    /// </summary>
    public static readonly DependencyProperty StackOffsetProperty =
        DependencyProperty.Register(nameof(StackOffset), typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(18d, FrameworkPropertyMetadataOptions.AffectsArrange));

    public double DayStart
    {
        get => (double)GetValue(DayStartProperty);
        set => SetValue(DayStartProperty, value);
    }

    public double DayEnd
    {
        get => (double)GetValue(DayEndProperty);
        set => SetValue(DayEndProperty, value);
    }

    public double MinuteHeight
    {
        get => (double)GetValue(MinuteHeightProperty);
        set => SetValue(MinuteHeightProperty, value);
    }

    public double Gap
    {
        get => (double)GetValue(GapProperty);
        set => SetValue(GapProperty, value);
    }

    public double MinimumHeight
    {
        get => (double)GetValue(MinimumHeightProperty);
        set => SetValue(MinimumHeightProperty, value);
    }

    public double MinimumWidth
    {
        get => (double)GetValue(MinimumWidthProperty);
        set => SetValue(MinimumWidthProperty, value);
    }

    public double StackOffset
    {
        get => (double)GetValue(StackOffsetProperty);
        set => SetValue(StackOffsetProperty, value);
    }

    // ==== Angaben zur einzelnen Karte ====

    public static readonly DependencyProperty StartMinutesProperty =
        DependencyProperty.RegisterAttached("StartMinutes", typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(0d,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty EndMinutesProperty =
        DependencyProperty.RegisterAttached("EndMinutes", typeof(double), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(0d,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty ColumnProperty =
        DependencyProperty.RegisterAttached("Column", typeof(int), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(0,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static readonly DependencyProperty ColumnCountProperty =
        DependencyProperty.RegisterAttached("ColumnCount", typeof(int), typeof(TimeGridPanel),
            new FrameworkPropertyMetadata(1,
                FrameworkPropertyMetadataOptions.AffectsParentMeasure |
                FrameworkPropertyMetadataOptions.AffectsParentArrange));

    public static void SetStartMinutes(UIElement element, double value) =>
        element.SetValue(StartMinutesProperty, value);

    public static double GetStartMinutes(UIElement element) =>
        (double)element.GetValue(StartMinutesProperty);

    public static void SetEndMinutes(UIElement element, double value) =>
        element.SetValue(EndMinutesProperty, value);

    public static double GetEndMinutes(UIElement element) =>
        (double)element.GetValue(EndMinutesProperty);

    public static void SetColumn(UIElement element, int value) =>
        element.SetValue(ColumnProperty, value);

    public static int GetColumn(UIElement element) =>
        (int)element.GetValue(ColumnProperty);

    public static void SetColumnCount(UIElement element, int value) =>
        element.SetValue(ColumnCountProperty, value);

    public static int GetColumnCount(UIElement element) =>
        (int)element.GetValue(ColumnCountProperty);

    // ==== Anordnen ====

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width;

        foreach (UIElement child in InternalChildren)
        {
            var slot = SlotOf(child, width);
            child.Measure(new Size(slot.Width, slot.Height));
        }

        return new Size(width, Math.Max(0, (DayEnd - DayStart) * MinuteHeight));
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (UIElement child in InternalChildren)
            child.Arrange(SlotOf(child, finalSize.Width));

        return finalSize;
    }

    /// <summary>
    /// Platz einer Karte: die Uhrzeit bestimmt oben und die Höhe, die Spalte die
    /// Lage waagrecht. Solange jede gleichzeitige Karte breit genug bleibt,
    /// stehen sie nebeneinander; sonst werden sie gestaffelt.
    /// </summary>
    private Rect SlotOf(UIElement child, double width)
    {
        var columns = Math.Max(1, GetColumnCount(child));
        var column = Math.Clamp(GetColumn(child), 0, columns - 1);

        var top = (GetStartMinutes(child) - DayStart) * MinuteHeight;
        var height = Math.Max(MinimumHeight, (GetEndMinutes(child) - GetStartMinutes(child)) * MinuteHeight);

        var (left, cardWidth) = columns == 1 || width / columns >= MinimumWidth
            ? SideBySide(width, columns, column)
            : Stacked(width, columns, column);

        return new Rect(left, Math.Max(0, top), Math.Max(0, cardWidth), height);
    }

    /// <summary>Genug Platz: gleich breite Karten nebeneinander, dazwischen der Abstand.</summary>
    private (double Left, double Width) SideBySide(double width, int columns, int column)
    {
        var columnWidth = width / columns;

        return (column * columnWidth, columnWidth - (column < columns - 1 ? Gap : 0));
    }

    /// <summary>
    /// Zu wenig Platz: die Karten liegen gestaffelt übereinander wie ein
    /// aufgefächerter Stapel Papier. Jede ist gleich breit und um einen
    /// Streifen nach rechts versetzt, die letzte schliesst rechts ab. Von jeder
    /// verdeckten Karte bleibt links dieser Streifen sichtbar - darüber kommt
    /// man mit der Maus an sie heran.
    ///
    /// Der Streifen ist mindestens <see cref="StackOffset"/> breit. Nur wenn
    /// selbst das nicht mehr aufginge, wird er schmaler: Weiter als bis zum
    /// gleichmässigen Nebeneinander darf der Versatz nicht wachsen, sonst
    /// bliebe von den Karten selbst weniger übrig als vom Streifen.
    /// </summary>
    private (double Left, double Width) Stacked(double width, int columns, int column)
    {
        var stacks = columns - 1;
        var step = Math.Clamp((width - MinimumWidth) / stacks, 0, width / columns);

        step = Math.Max(step, Math.Min(StackOffset, width / columns));

        return (column * step, width - stacks * step);
    }
}
