using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace SchoolManager.App.Data;

/// <summary>
/// Hält die Hausaufgaben. Die Hausaufgaben-Seite bearbeitet sie, der Kalender
/// zeigt ihre Abgabetermine an.
/// </summary>
public sealed class HomeworkStore
{
    private const string FileName = "hausaufgaben.json";

    public HomeworkStore()
    {
        Items = new ObservableCollection<Homework>(LocalStore.Load<Homework>(FileName));

        foreach (var item in Items)
            item.PropertyChanged += Item_PropertyChanged;

        Items.CollectionChanged += Items_CollectionChanged;
    }

    public ObservableCollection<Homework> Items { get; }

    public event Action? Changed;

    public event Action<string>? SaveFailed;

    public Homework Add(string title)
    {
        var homework = new Homework { Title = title };
        Items.Insert(0, homework);
        return homework;
    }

    public void Remove(Homework homework) => Items.Remove(homework);

    /// <summary>Hausaufgaben, deren Abgabetermin im Zeitraum liegt.</summary>
    public IEnumerable<Homework> DueBetween(DateTime from, DateTime to) =>
        Items.Where(item => item.DueDate is { } due &&
                            due.LocalDateTime.Date >= from.Date &&
                            due.LocalDateTime.Date < to.Date);

    public void Save()
    {
        if (LocalStore.TrySave(FileName, Items) is { } error)
            SaveFailed?.Invoke(error);
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<Homework>() ?? [])
            item.PropertyChanged -= Item_PropertyChanged;

        foreach (var item in e.NewItems?.OfType<Homework>() ?? [])
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
