using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Hausaufgaben mit Fach, Termin und einem Verweis auf einen Auftrag, eine
/// Aufgabe oder ein Leistungsdetail.
/// </summary>
public partial class HomeworkPage : UserControl
{
    private readonly ObservableCollection<Homework> items;
    private readonly ICollectionView view;
    private readonly WorkStore workStore;
    private readonly IStatusSink status;

    /// <summary>Verhindert, dass das Nachführen der Auswahlliste als Eingabe zählt.</summary>
    private bool suppressLinkChange;

    private readonly HomeworkStore homeworkStore;

    public HomeworkPage(WorkStore workStore, HomeworkStore homeworkStore, IStatusSink status)
    {
        this.workStore = workStore;
        this.homeworkStore = homeworkStore;
        this.status = status;

        InitializeComponent();

        items = homeworkStore.Items;

        foreach (var item in items)
            item.PropertyChanged += Item_PropertyChanged;

        items.CollectionChanged += Items_CollectionChanged;

        view = CollectionViewSource.GetDefaultView(items);
        view.Filter = Matches;
        view.SortDescriptions.Add(new SortDescription(nameof(Homework.IsDone), ListSortDirection.Ascending));
        HomeworkList.ItemsSource = view;

        FilterBox.ItemsSource = HomeworkFilter.All;
        FilterBox.SelectedIndex = 0;

        RebuildLinkChoices();

        if (!view.IsEmpty)
            HomeworkList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Bittet die Seitennavigation, den verknüpften Eintrag zu öffnen.</summary>
    public event Action<WorkNode>? OpenNodeRequested;

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        // Aufträge und Aufgaben können sich zwischenzeitlich geändert haben.
        RebuildLinkChoices();
        ShowLinkOfSelection();
        UpdateSummary();
        NewTitleBox.Focus();
    }

    private Homework? Selected => HomeworkList.SelectedItem as Homework;

    private HomeworkFilter SelectedFilter => FilterBox.SelectedItem as HomeworkFilter ?? HomeworkFilter.All[0];

    private bool Matches(object item) => item is Homework homework && SelectedFilter.Matches(homework);

    // ==== Anlegen und Löschen ====

    private void NewTitleBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Add();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Add();

    private void Add()
    {
        var title = NewTitleBox.Text.Trim();

        if (title.Length == 0)
        {
            status.SetStatus("Bitte zuerst einen Titel für die Hausaufgabe eingeben.", StatusKind.Error);
            NewTitleBox.Focus();
            return;
        }

        var homework = homeworkStore.Add(title);
        NewTitleBox.Clear();

        HomeworkList.SelectedItem = homework;
        status.SetStatus("Hausaufgabe hinzugefügt.", StatusKind.Success);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } homework)
            return;

        if (MessageBox.Show(Window.GetWindow(this)!, $"„{homework.DisplayTitle}“ löschen?",
                "Hausaufgabe löschen", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        homeworkStore.Remove(homework);
        status.SetStatus("Hausaufgabe gelöscht.", StatusKind.Info);
    }

    // ==== Verweis ====

    private void RebuildLinkChoices()
    {
        var choices = new List<LinkChoice> { new(null) };
        choices.AddRange(workStore.AllNodes().Select(node => new LinkChoice(node)));

        suppressLinkChange = true;
        LinkBox.ItemsSource = choices;
        suppressLinkChange = false;

        ShowLinkOfSelection();
    }

    private void ShowLinkOfSelection()
    {
        var choices = LinkBox.ItemsSource as List<LinkChoice> ?? [];
        var node = workStore.FindById(Selected?.LinkId);

        suppressLinkChange = true;
        LinkBox.SelectedItem = choices.FirstOrDefault(choice => ReferenceEquals(choice.Node, node)) ?? choices[0];
        suppressLinkChange = false;

        OpenLinkButton.IsEnabled = node is not null;

        LinkInfoText.Text = Selected is null
            ? ""
            : node is null
                ? string.IsNullOrEmpty(Selected.LinkId)
                    ? "Kein Verweis gesetzt."
                    : "Der verknüpfte Eintrag wurde gelöscht."
                : $"{node.LevelName} · {node.EstimateSummary}";
    }

    private void LinkBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (suppressLinkChange || Selected is not { } homework)
            return;

        homework.LinkId = (LinkBox.SelectedItem as LinkChoice)?.Node?.Id;
        ShowLinkOfSelection();

        status.SetStatus(
            homework.LinkId is null ? "Verweis entfernt." : "Verweis gesetzt.",
            StatusKind.Success);
    }

    private void OpenLink_Click(object sender, RoutedEventArgs e)
    {
        if (workStore.FindById(Selected?.LinkId) is { } node)
            OpenNodeRequested?.Invoke(node);
    }

    // ==== Oberfläche ====

    private void DueBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox { Text.Length: > 0 } box && !DateTime.TryParse(box.Text,
                CultureInfo.GetCultureInfo("de-CH"), DateTimeStyles.None, out _))
            status.SetStatus($"„{box.Text}“ ist kein Datum - erwartet wird tt.mm.jjjj.", StatusKind.Error);
    }

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        view.Refresh();
        UpdateSummary();
    }

    private void HomeworkList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowLinkOfSelection();
        UpdateSummary();
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<Homework>() ?? [])
            item.PropertyChanged -= Item_PropertyChanged;

        foreach (var item in e.NewItems?.OfType<Homework>() ?? [])
            item.PropertyChanged += Item_PropertyChanged;

        UpdateSummary();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Homework.IsDone) or nameof(Homework.DueDate))
            view.Refresh();

        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var open = items.Count(item => !item.IsDone);
        var overdue = items.Count(item => item.IsOverdue);

        SummaryText.Text = items.Count == 0
            ? "Keine Hausaufgaben"
            : $"{items.Count} {(items.Count == 1 ? "Hausaufgabe" : "Hausaufgaben")} · {open} offen"
              + (overdue > 0 ? $" · {overdue} überfällig" : "");

        var hasSelection = Selected is not null;

        DeleteButton.IsEnabled = hasSelection;
        DetailScroller.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        EmptyListText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Ein Eintrag der Verweis-Auswahl.</summary>
    public sealed class LinkChoice(WorkNode? node)
    {
        public WorkNode? Node { get; } = node;

        public override string ToString() =>
            Node is null ? "— kein Verweis —" : $"{Node.LevelName}: {Node.Path}";
    }

    /// <summary>Ein Eintrag der Filter-Auswahl.</summary>
    public sealed class HomeworkFilter(string label, Func<Homework, bool> predicate)
    {
        public bool Matches(Homework homework) => predicate(homework);

        public override string ToString() => label;

        public static HomeworkFilter[] All { get; } =
        [
            new("Alle", _ => true),
            new("Offen", h => !h.IsDone),
            new("Überfällig", h => h.IsOverdue),
            new("Erledigt", h => h.IsDone)
        ];
    }
}
