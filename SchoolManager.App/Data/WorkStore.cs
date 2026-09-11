using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows.Threading;

namespace SchoolManager.App.Data;

/// <summary>
/// Hält die Aufträge mit ihren Aufgaben und Leistungsdetails. Alle Seiten
/// arbeiten auf denselben Daten; gespeichert wird kurz nach der letzten
/// Änderung, spätestens beim Seitenwechsel oder beim Schliessen.
/// </summary>
public sealed class WorkStore
{
    private const string FileName = "auftraege.json";

    private readonly DispatcherTimer saveTimer;
    private bool savePending;

    public WorkStore()
    {
        Orders = new ObservableCollection<WorkOrder>(LocalStore.Load<WorkOrder>(FileName));

        foreach (var order in Orders)
            Attach(order);

        Orders.CollectionChanged += Orders_CollectionChanged;

        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        saveTimer.Tick += (_, _) => Flush();

        RebuildTasks();
    }

    public ObservableCollection<WorkOrder> Orders { get; }

    /// <summary>Alle Aufgaben über alle Aufträge - für die Gesamtübersicht.</summary>
    public ObservableCollection<WorkTask> Tasks { get; } = [];

    /// <summary>Irgendetwas hat sich geändert; Zusammenfassungen neu berechnen.</summary>
    public event Action? Changed;

    /// <summary>Speichern ist fehlgeschlagen, mit Meldung.</summary>
    public event Action<string>? SaveFailed;

    public int TotalMinutes => Orders.Sum(order => order.TotalMinutes);

    public int EntryCount => Orders.Sum(order => order.Tasks.Sum(task => task.Entries.Count));

    /// <summary>Legt einen Auftrag an und gibt ihn zurück.</summary>
    public WorkOrder AddOrder()
    {
        // Auftrag und Aufgabe bekommen von Anfang an ein Enddatum.
        var order = new WorkOrder
        {
            Title = "Neuer Auftrag",
            DueDate = new DateTimeOffset(DateTime.Today.AddDays(14))
        };
        Orders.Add(order);
        return order;
    }

    /// <summary>
    /// Legt eine Aufgabe im Auftrag an. Auftraggeber und Fach kommen vom
    /// Auftrag und sind danach frei änderbar; mit <paramref name="client"/>
    /// lässt sich der Auftraggeber vorgeben - etwa die Lehrkraft der gerade
    /// laufenden Lektion.
    /// </summary>
    public WorkTask AddTask(WorkOrder order, string? client = null)
    {
        var task = new WorkTask
        {
            Title = "Neue Aufgabe",
            Client = string.IsNullOrWhiteSpace(client) ? order.Client : client.Trim(),
            Subject = order.Subject,
            DueDate = order.DueDate ?? new DateTimeOffset(DateTime.Today.AddDays(7))
        };

        order.Tasks.Add(task);
        return task;
    }

    /// <summary>Legt ein Leistungsdetail in der Aufgabe an, mit deren Angaben.</summary>
    public WorkEntry AddEntry(WorkTask task)
    {
        var entry = new WorkEntry
        {
            Title = "Neues Leistungsdetail",
            Client = task.Client,
            Subject = task.Subject
        };

        task.Entries.Add(entry);
        return entry;
    }

    public void Remove(WorkNode node)
    {
        switch (node)
        {
            case WorkOrder order:
                Orders.Remove(order);
                break;

            case WorkTask task when task.Parent is WorkOrder parent:
                parent.Tasks.Remove(task);
                break;

            case WorkEntry entry when entry.Parent is WorkTask parent:
                parent.Entries.Remove(entry);
                break;
        }
    }

    /// <summary>Alle Einträge aller drei Ebenen, von oben nach unten.</summary>
    public IEnumerable<WorkNode> AllNodes()
    {
        foreach (var order in Orders)
        {
            yield return order;

            foreach (var task in order.Tasks)
            {
                yield return task;

                foreach (var entry in task.Entries)
                    yield return entry;
            }
        }
    }

    public WorkNode? FindById(string? id) =>
        string.IsNullOrEmpty(id) ? null : AllNodes().FirstOrDefault(node => node.Id == id);

    /// <summary>Schreibt offene Änderungen sofort auf die Festplatte.</summary>
    public void Flush()
    {
        saveTimer.Stop();

        if (!savePending)
            return;

        savePending = false;

        if (LocalStore.TrySave(FileName, Orders) is { } error)
            SaveFailed?.Invoke(error);
    }

    private void Orders_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var order in e.OldItems?.OfType<WorkOrder>() ?? [])
            Detach(order);

        foreach (var order in e.NewItems?.OfType<WorkOrder>() ?? [])
            Attach(order);

        RebuildTasks();
        OnChanged();
    }

    private void Attach(WorkOrder order)
    {
        order.Changed += OnChanged;
        order.Tasks.CollectionChanged += Tasks_CollectionChanged;
    }

    private void Detach(WorkOrder order)
    {
        order.Changed -= OnChanged;
        order.Tasks.CollectionChanged -= Tasks_CollectionChanged;
    }

    private void Tasks_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildTasks();
        OnChanged();
    }

    /// <summary>
    /// Baut die flache Aufgabenliste neu auf. Bei dieser Grössenordnung ist das
    /// einfacher und verlässlicher, als einzelne Einfügungen nachzuführen.
    /// </summary>
    private void RebuildTasks()
    {
        var current = Orders.SelectMany(order => order.Tasks).ToList();

        if (current.Count == Tasks.Count && !current.Where((task, i) => !ReferenceEquals(task, Tasks[i])).Any())
            return;

        Tasks.Clear();

        foreach (var task in current)
            Tasks.Add(task);
    }

    private void OnChanged()
    {
        savePending = true;
        Changed?.Invoke();

        // Timer neu starten: gespeichert wird kurz nach der letzten Eingabe.
        saveTimer.Stop();
        saveTimer.Start();
    }
}
