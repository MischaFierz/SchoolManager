using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace SchoolManager.App.Controls;

/// <summary>
/// Lässt das Mausrad weiterrollen, auch wenn der Zeiger gerade über einem
/// Bedienelement steht, das es für sich behält.
///
/// Der Grund für den Fehler: WPF meldet ein Mausrad-Ereignis als erledigt,
/// sobald es bei einem Auswahlfeld oder einem eigenen Rollbereich ankommt -
/// und zwar auch dann, wenn dieses damit gar nichts anfangen kann. Auf einer
/// Seite mit Auswahlfeldern bleibt das Blättern deshalb hängen, sobald der
/// Zeiger nach ein paar Zeilen über dem ersten Feld steht. Es sieht aus, als
/// klemme der Rollbalken, dabei kommt das Ereignis schlicht nie oben an.
///
/// Angeschaltet wird das Ganze am Rollbereich selbst:
/// <code>&lt;ScrollViewer c:WheelScroll.Forward="True"&gt;</code>
///
/// Ein echter Rollbereich weiter innen behält den Vortritt, solange er selbst
/// noch Platz in diese Richtung hat - ein langes Notizfeld lässt sich also
/// weiterhin für sich blättern, und erst an seinem Ende rollt die Seite weiter.
/// </summary>
public static class WheelScroll
{
    public static readonly DependencyProperty ForwardProperty =
        DependencyProperty.RegisterAttached("Forward", typeof(bool), typeof(WheelScroll),
            new PropertyMetadata(false, OnForwardChanged));

    public static void SetForward(DependencyObject element, bool value) =>
        element.SetValue(ForwardProperty, value);

    public static bool GetForward(DependencyObject element) =>
        (bool)element.GetValue(ForwardProperty);

    private static void OnForwardChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not ScrollViewer scroller)
            return;

        // Vorschau, nicht das gewöhnliche Ereignis: Nur auf dem Weg von oben
        // nach unten kommt man vor dem Element an, das sonst alles abfängt.
        if ((bool)e.NewValue)
            scroller.PreviewMouseWheel += Wheel;
        else
            scroller.PreviewMouseWheel -= Wheel;
    }

    private static void Wheel(object sender, MouseWheelEventArgs e)
    {
        if (e.Handled || sender is not ScrollViewer scroller)
            return;

        if (InnerScrollerHasRoom(e.OriginalSource as DependencyObject, scroller, e.Delta))
            return;

        // Genauso weit wie überall sonst: drei Zeilen je Rasterschritt des
        // Rades. Ein eigener Rechenweg über den Versatz rollte spürbar anders
        // als der Rest der Anwendung.
        var lines = Math.Max(1, Math.Abs(e.Delta) / 40);

        for (var line = 0; line < lines; line++)
        {
            if (e.Delta > 0)
                scroller.LineUp();
            else
                scroller.LineDown();
        }

        e.Handled = true;
    }

    /// <summary>
    /// Gibt es zwischen dem angeklickten Element und diesem Rollbereich einen
    /// weiteren, der in diese Richtung noch rollen kann?
    /// </summary>
    private static bool InnerScrollerHasRoom(DependencyObject? source, ScrollViewer outer, int delta)
    {
        for (var node = source; node is not null && node != outer; node = ParentOf(node))
        {
            if (node is not ScrollViewer inner || inner.ScrollableHeight <= 0)
                continue;

            // Rad nach oben rollt zurück, nach unten weiter.
            return delta > 0
                ? inner.VerticalOffset > 0
                : inner.VerticalOffset < inner.ScrollableHeight;
        }

        return false;
    }

    /// <summary>
    /// Der Weg nach oben. Der Auslöser ist nicht immer ein sichtbares Element -
    /// ein Textabschnitt etwa hängt nur im logischen Baum; für den ist
    /// VisualTreeHelper nicht zuständig und würde werfen.
    /// </summary>
    private static DependencyObject? ParentOf(DependencyObject node) =>
        node is Visual or Visual3D
            ? VisualTreeHelper.GetParent(node)
            : LogicalTreeHelper.GetParent(node);
}
