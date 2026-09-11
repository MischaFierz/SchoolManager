using System.Windows;
using System.Windows.Controls;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Aufträge: links die Liste, rechts der ausgewählte Auftrag mit seinen
/// Angaben und der Liste seiner Aufgaben.
/// </summary>
public partial class OrdersPage : UserControl
{
    private readonly WorkStore store;
    private readonly CalendarFeed feed;
    private readonly IStatusSink status;

    /// <summary>
    /// Der zuletzt gewählte Auftrag. Wandert er durch den Fertig-Haken in den
    /// anderen Abschnitt, baut die Liste diese Zeile neu auf und verliert dabei
    /// die Auswahl; dann wird sie hier wiederhergestellt.
    /// </summary>
    private WorkOrder? lastSelected;

    public OrdersPage(WorkStore store, CalendarFeed feed, IStatusSink status)
    {
        this.store = store;
        this.feed = feed;
        this.status = status;

        InitializeComponent();

        // Zwei Abschnitte: offene Aufträge oben, abgeschlossene darunter.
        OrderList.ItemsSource = DoneGrouping.ByDone(store.Orders);
        store.Changed += UpdateSummary;

        if (store.Orders.Count > 0)
            OrderList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Bittet die Seitennavigation, diese Aufgabe zu öffnen.</summary>
    public event Action<WorkTask>? OpenTaskRequested;

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        UpdateSummary();
        OrderList.Focus();
    }

    /// <summary>Wählt einen Auftrag aus - etwa aus einem Verweis der Hausaufgaben.</summary>
    public void Select(WorkOrder order)
    {
        OrderList.SelectedItem = order;
        OrderList.ScrollIntoView(order);
    }

    private WorkOrder? SelectedOrder => OrderList.SelectedItem as WorkOrder;

    private void NewOrder_Click(object sender, RoutedEventArgs e)
    {
        var order = store.AddOrder();
        Select(order);
        status.SetStatus("Auftrag angelegt.", StatusKind.Success);
    }

    private void DeleteOrder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOrder is not { } order)
            return;

        var question = order.Tasks.Count > 0
            ? $"„{order.DisplayTitle}“ mit {order.Tasks.Count} {(order.Tasks.Count == 1 ? "Aufgabe" : "Aufgaben")} und allen Leistungsdetails löschen?"
            : $"„{order.DisplayTitle}“ löschen?";

        if (MessageBox.Show(Window.GetWindow(this)!, question, "Auftrag löschen",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        var index = store.Orders.IndexOf(order);
        store.Remove(order);

        if (store.Orders.Count > 0)
            OrderList.SelectedIndex = Math.Min(index, store.Orders.Count - 1);

        status.SetStatus("Auftrag gelöscht.", StatusKind.Info);
        UpdateSummary();
    }

    private void NewTask_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedOrder is not { } order)
            return;

        // Läuft gerade eine Lektion, wird deren Lehrkraft als Auftraggeber eingesetzt.
        var lesson = feed.CurrentLessonWithTeacher(DateTime.Now);
        var task = store.AddTask(order, lesson?.TeacherName);

        UpdateSummary();

        status.SetStatus(
            lesson is null
                ? $"Aufgabe in „{order.DisplayTitle}“ angelegt."
                : $"Aufgabe angelegt - Auftraggeber aus der laufenden Lektion {lesson.Title}: {lesson.TeacherName}.",
            StatusKind.Success);

        // Gleich dort weiterarbeiten, wo die Leistungsdetails erfasst werden.
        OpenTaskRequested?.Invoke(task);
    }

    private void OpenTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkTask task })
            OpenTaskRequested?.Invoke(task);
    }

    private void DurationBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox box && !TimeText.TryParse(box.Text, out _))
            status.SetStatus(
                $"„{box.Text}“ ist keine Zeitangabe - erlaubt sind etwa 90, 1:30 oder 1,5h.",
                StatusKind.Error);
    }

    private void OrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedOrder is { } selected)
            lastSelected = selected;

        // Beim Wechsel oben anfangen, nicht mitten im Formular.
        DetailScroller.ScrollToTop();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        // Ein Wechsel zwischen den Abschnitten nimmt der Liste die Auswahl.
        if (OrderList.SelectedItem is null && lastSelected is { } previous && store.Orders.Contains(previous))
        {
            OrderList.SelectedItem = previous;
            OrderList.ScrollIntoView(previous);
        }

        var orders = store.Orders;
        var taskCount = orders.Sum(order => order.Tasks.Count);
        var openOrders = orders.Count(order => !order.IsDone);

        SummaryText.Text = orders.Count == 0
            ? "Keine Aufträge"
            : $"{Count(orders.Count, "Auftrag", "Aufträge")} · {openOrders} offen · " +
              $"{Count(taskCount, "Aufgabe", "Aufgaben")} · " +
              $"gesamt {TimeText.FormatBoth(store.TotalMinutes)}";

        var hasSelection = SelectedOrder is not null;

        DeleteOrderButton.IsEnabled = hasSelection;
        DetailScroller.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        EmptyListText.Visibility = orders.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoTasksText.Visibility = SelectedOrder?.Tasks.Count is 0 or null
            ? Visibility.Visible
            : Visibility.Collapsed;

        static string Count(int value, string singular, string plural) =>
            $"{value} {(value == 1 ? singular : plural)}";
    }
}
