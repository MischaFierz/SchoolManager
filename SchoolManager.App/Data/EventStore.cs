using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchoolManager.App.Data;

/// <summary>
/// Hält die selbst erfassten Kalendereinträge - Prüfungen und Termine. Wird von
/// der Prüfungsseite und vom Kalender gemeinsam benutzt.
/// </summary>
public sealed class EventStore
{
    private const string FileName = "termine.json";

    public EventStore()
    {
        Items = new ObservableCollection<CalendarEvent>(
            LocalStore.Load<CalendarEvent>(FileName).OrderBy(item => item.Start));

        foreach (var item in Items)
            item.PropertyChanged += Item_PropertyChanged;

        Items.CollectionChanged += Items_CollectionChanged;
    }

    public ObservableCollection<CalendarEvent> Items { get; }

    public event Action? Changed;

    public event Action<string>? SaveFailed;

    public IEnumerable<CalendarEvent> Exams => Items.Where(item => item.Kind == CalendarEventKind.Pruefung);

    /// <summary>Legt einen Eintrag an und gibt ihn zurück.</summary>
    public CalendarEvent Add(CalendarEventKind kind)
    {
        var item = new CalendarEvent
        {
            Kind = kind,
            Start = new DateTimeOffset(DateTime.Today.AddDays(7).AddHours(8)),
            DurationMinutes = kind == CalendarEventKind.Pruefung ? 45 : 60
        };

        Items.Add(item);
        return item;
    }

    public void Remove(CalendarEvent item) => Items.Remove(item);

    /// <summary>Alle Einträge, die im Zeitraum beginnen.</summary>
    public IEnumerable<CalendarEvent> Between(DateTime from, DateTime to) =>
        Items.Where(item => item.Start.LocalDateTime >= from && item.Start.LocalDateTime < to);

    /// <summary>Nimmt eingelesene ICS-Termine als eigene Einträge auf.</summary>
    public int ImportLessons(IEnumerable<Lesson> lessons, CalendarEventKind kind)
    {
        var added = 0;

        foreach (var lesson in lessons)
        {
            var exists = Items.Any(item =>
                item.Start.LocalDateTime == lesson.Start &&
                string.Equals(item.DisplayTitle, lesson.Subject, StringComparison.OrdinalIgnoreCase));

            if (exists)
                continue;

            Items.Add(new CalendarEvent
            {
                Kind = kind,
                Title = lesson.Subject,
                Room = lesson.Location,
                Notes = lesson.Description,
                Start = new DateTimeOffset(lesson.Start),
                DurationMinutes = (int)(lesson.End - lesson.Start).TotalMinutes,
                IsAllDay = lesson.IsAllDay
            });

            added++;
        }

        return added;
    }

    public void Save()
    {
        if (LocalStore.TrySave(FileName, Items) is { } error)
            SaveFailed?.Invoke(error);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<CalendarEvent>() ?? [])
            item.PropertyChanged -= Item_PropertyChanged;

        foreach (var item in e.NewItems?.OfType<CalendarEvent>() ?? [])
            item.PropertyChanged += Item_PropertyChanged;

        Changed?.Invoke();
        Save();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke();
        Save();
    }
}
