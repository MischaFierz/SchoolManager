using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Übersicht aller Aufgaben über alle Aufträge; rechts die ausgewählte Aufgabe
/// mit der Liste ihrer Leistungsdetails, in der die Zeit erfasst wird.
/// </summary>
public partial class TasksPage : UserControl
{
    private readonly WorkStore store;
    private readonly CalendarFeed feed;
    private readonly IStatusSink status;

    /// <summary>
    /// Die zuletzt gewählte Aufgabe. Wandert sie durch den Fertig-Haken in den
    /// anderen Abschnitt, baut die Liste diese Zeile neu auf und verliert dabei
    /// die Auswahl; dann wird sie hier wiederhergestellt, damit rechts weiter
    /// dieselbe Aufgabe steht.
    /// </summary>
    private WorkTask? lastSelected;

    public TasksPage(WorkStore store, CalendarFeed feed, IStatusSink status)
    {
        this.store = store;
        this.feed = feed;
        this.status = status;

        InitializeComponent();

        // Zwei Abschnitte: offene Aufgaben oben, abgeschlossene darunter.
        TaskList.ItemsSource = DoneGrouping.ByDone(store.Tasks);
        OrderBox.ItemsSource = store.Orders;

        store.Changed += UpdateSummary;

        if (store.Tasks.Count > 0)
            TaskList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        UpdateSummary();
        TaskList.Focus();
    }

    /// <summary>Wählt eine Aufgabe aus - etwa aus dem Auftrag oder den Hausaufgaben heraus.</summary>
    public void Select(WorkTask task)
    {
        TaskList.SelectedItem = task;
        TaskList.ScrollIntoView(task);
    }

    private WorkTask? SelectedTask => TaskList.SelectedItem as WorkTask;

    // ==== Anlegen und Löschen ====

    private void NewTask_Click(object sender, RoutedEventArgs e)
    {
        if (OrderBox.SelectedItem is not WorkOrder order)
        {
            status.SetStatus("Bitte zuerst oben den Auftrag wählen.", StatusKind.Error);
            OrderBox.Focus();
            return;
        }

        // Läuft gerade eine Lektion, wird deren Lehrkraft als Auftraggeber eingesetzt.
        var lesson = feed.CurrentLessonWithTeacher(DateTime.Now);
        var task = store.AddTask(order, lesson?.TeacherName);

        Select(task);

        status.SetStatus(
            lesson is null
                ? $"Aufgabe in „{order.DisplayTitle}“ angelegt."
                : $"Aufgabe angelegt - Auftraggeber aus der laufenden Lektion {lesson.Title}: {lesson.TeacherName}.",
            StatusKind.Success);
    }

    private void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTask is not { } task)
            return;

        var question = task.Entries.Count > 0
            ? $"„{task.DisplayTitle}“ mit {task.Entries.Count} {(task.Entries.Count == 1 ? "Leistungsdetail" : "Leistungsdetails")} löschen?"
            : $"„{task.DisplayTitle}“ löschen?";

        if (MessageBox.Show(Window.GetWindow(this)!, question, "Aufgabe löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var index = store.Tasks.IndexOf(task);
        store.Remove(task);

        if (store.Tasks.Count > 0)
            TaskList.SelectedIndex = Math.Min(index, store.Tasks.Count - 1);

        status.SetStatus("Aufgabe gelöscht.", StatusKind.Info);
        UpdateSummary();
    }

    private void NewEntry_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedTask is not { } task)
            return;

        store.AddEntry(task);
        UpdateSummary();
        status.SetStatus("Leistungsdetail angelegt - Dauer eintragen.", StatusKind.Success);
    }

    private void DeleteEntry_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkEntry entry })
            return;

        store.Remove(entry);
        UpdateSummary();
        status.SetStatus("Leistungsdetail gelöscht.", StatusKind.Info);
    }

    /// <summary>Übernimmt Auftraggeber und Fach von der übergeordneten Ebene.</summary>
    private void Inherit_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: WorkNode node })
            return;

        if (node.Parent is null)
        {
            status.SetStatus("Dieser Eintrag hat keine übergeordnete Ebene.", StatusKind.Error);
            return;
        }

        node.InheritFromParent();
        status.SetStatus("Auftraggeber und Fach übernommen.", StatusKind.Success);
    }

    // ==== Eingabefelder mit eigener Prüfung ====

    private void DurationBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !TimeText.TryParse(box.Text, out _))
            status.SetStatus(
                $"„{box.Text}“ ist keine Zeitangabe - erlaubt sind etwa 90, 1:30 oder 1,5h.",
                StatusKind.Error);
    }

    private void DueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Text.Length: > 0 } box && !DateTime.TryParse(box.Text,
                CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out _))
            status.SetStatus($"„{box.Text}“ ist kein Datum - erwartet wird tt.mm.jjjj.", StatusKind.Error);
    }

    private void DateBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !DateTime.TryParse(box.Text,
                CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out _))
            status.SetStatus($"„{box.Text}“ ist kein Datum - erwartet wird tt.mm.jjjj.", StatusKind.Error);
    }

    // ==== Oberfläche ====

    private void TaskList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedTask is { } selected)
            lastSelected = selected;

        // Der Auftrag der ausgewählten Aufgabe ist die naheliegende Vorauswahl.
        if (SelectedTask?.Parent is WorkOrder order)
            OrderBox.SelectedItem = order;

        // Beim Wechsel oben anfangen, nicht mitten im Formular.
        DetailScroller.ScrollToTop();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        // Ein Wechsel zwischen den Abschnitten nimmt der Liste die Auswahl.
        if (TaskList.SelectedItem is null && lastSelected is { } previous && store.Tasks.Contains(previous))
        {
            TaskList.SelectedItem = previous;
            TaskList.ScrollIntoView(previous);
        }

        var tasks = store.Tasks;
        var open = tasks.Count(task => !task.IsDone);

        SummaryText.Text = tasks.Count == 0
            ? "Keine Aufgaben"
            : $"{Count(tasks.Count, "Aufgabe", "Aufgaben")} · {open} offen · " +
              $"{Count(store.EntryCount, "Leistungsdetail", "Leistungsdetails")} · " +
              $"gesamt {TimeText.FormatBoth(store.TotalMinutes)}";

        static string Count(int value, string singular, string plural) =>
            $"{value} {(value == 1 ? singular : plural)}";

        if (OrderBox.SelectedItem is null && store.Orders.Count > 0)
            OrderBox.SelectedItem = SelectedTask?.Parent ?? store.Orders[0];

        var hasSelection = SelectedTask is not null;

        DeleteTaskButton.IsEnabled = hasSelection;
        NewTaskButton.IsEnabled = store.Orders.Count > 0;
        DetailScroller.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        EmptyListText.Visibility = tasks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoEntriesText.Visibility = SelectedTask?.Entries.Count is 0 or null
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
