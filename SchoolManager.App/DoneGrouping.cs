using System.Collections;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;
using SchoolManager.App.Data;

namespace SchoolManager.App;

/// <summary>
/// Teilt eine Liste von Aufträgen bzw. Aufgaben in zwei Abschnitte: oben die
/// offenen, darunter die abgeschlossenen. Ein Haken verschiebt den Eintrag
/// sofort in den anderen Abschnitt, ohne dass die Liste neu aufgebaut wird.
/// </summary>
public static class DoneGrouping
{
    /// <summary>
    /// Die Ansicht zur Liste, gruppiert nach der Fertig-Markierung. Innerhalb
    /// eines Abschnitts bleibt die ursprüngliche Reihenfolge erhalten - es wird
    /// nur gruppiert, nicht sortiert.
    /// </summary>
    public static ICollectionView ByDone(IEnumerable items)
    {
        var view = new CollectionViewSource { Source = items }.View;

        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(WorkNode.IsDone))
        {
            // Ohne das stünde zuoberst, was zufällig zuerst vorkommt.
            CustomSort = OpenFirst.Instance
        });

        // Live-Gruppierung: ein Klick auf das Kästchen schiebt den Eintrag
        // sofort hinüber, statt erst beim nächsten Aufbau der Liste.
        if (view is ICollectionViewLiveShaping { CanChangeLiveGrouping: true } live)
        {
            live.LiveGroupingProperties.Add(nameof(WorkNode.IsDone));
            live.IsLiveGrouping = true;
        }

        return view;
    }

    /// <summary>Offen (false) steht vor abgeschlossen (true).</summary>
    private sealed class OpenFirst : IComparer
    {
        public static OpenFirst Instance { get; } = new();

        public int Compare(object? x, object? y) => Key(x).CompareTo(Key(y));

        // Je nach WPF-Fassung kommt hier der Gruppenname oder die Gruppe selbst an.
        private static int Key(object? value) => value switch
        {
            bool done => done ? 1 : 0,
            CollectionViewGroup group => group.Name is true ? 1 : 0,
            _ => 0
        };
    }
}

/// <summary>Beschriftet die beiden Abschnitte; der Gruppenname ist IsDone.</summary>
public sealed class DoneGroupNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? "Abgeschlossen" : "Offen";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
