using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>Aufgabenliste; wird sofort nach jeder Änderung gespeichert.</summary>
public partial class TodoPage : UserControl
{
    private const string FileName = "todos.json";

    private readonly ObservableCollection<TodoItem> todos;
    private readonly ICollectionView view;
    private readonly IStatusSink status;

    public TodoPage(IStatusSink status)
    {
        this.status = status;

        InitializeComponent();

        todos = new ObservableCollection<TodoItem>(LocalStore.Load<TodoItem>(FileName));

        foreach (var item in todos)
            item.PropertyChanged += Item_PropertyChanged;

        todos.CollectionChanged += Todos_CollectionChanged;

        view = CollectionViewSource.GetDefaultView(todos);
        view.Filter = FilterTodo;
        TodoList.ItemsSource = view;

        FilterBox.ItemsSource = TodoFilter.All;
        FilterBox.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate() => NewTodoBox.Focus();

    private TodoFilter SelectedFilter => FilterBox.SelectedItem as TodoFilter ?? TodoFilter.All[0];

    private bool FilterTodo(object item) =>
        item is TodoItem todo && SelectedFilter.Matches(todo);

    private void FilterBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        view.Refresh();
        UpdateSummary();
    }

    private void NewTodoBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        AddTodo();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => AddTodo();

    private void AddTodo()
    {
        var text = NewTodoBox.Text.Trim();

        if (text.Length == 0)
        {
            status.SetStatus("Bitte zuerst einen Text für die Aufgabe eingeben.", StatusKind.Error);
            NewTodoBox.Focus();
            return;
        }

        var todo = new TodoItem { Text = text, Subject = NewSubjectBox.Text.Trim() };
        var dueGiven = NewDueBox.Text.Trim().Length > 0;

        // Über DueText gesetzt, damit ein unlesbares Datum hier genauso
        // behandelt wird wie beim Bearbeiten in der Liste: Es gilt als keines.
        todo.DueText = NewDueBox.Text.Trim();

        todos.Insert(0, todo);

        NewTodoBox.Clear();
        NewSubjectBox.Clear();
        NewDueBox.Text = "";
        NewTodoBox.Focus();

        var dateLost = dueGiven && todo.DueDate is null;

        status.SetStatus(
            dateLost
                ? "Aufgabe hinzugefügt - das Datum war nicht lesbar und wurde weggelassen."
                : "Aufgabe hinzugefügt.",
            dateLost ? StatusKind.Error : StatusKind.Success);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: TodoItem todo })
        {
            todos.Remove(todo);
            status.SetStatus("Aufgabe gelöscht.", StatusKind.Info);
        }
    }

    private void ClearDone_Click(object sender, RoutedEventArgs e)
    {
        var done = todos.Where(t => t.IsDone).ToList();

        if (done.Count == 0)
        {
            status.SetStatus("Es ist keine Aufgabe als erledigt markiert.", StatusKind.Info);
            return;
        }

        foreach (var todo in done)
            todos.Remove(todo);

        status.SetStatus($"{done.Count} erledigte Aufgabe(n) entfernt.", StatusKind.Success);
    }

    private void Todos_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<TodoItem>() ?? [])
            item.PropertyChanged -= Item_PropertyChanged;

        foreach (var item in e.NewItems?.OfType<TodoItem>() ?? [])
            item.PropertyChanged += Item_PropertyChanged;

        UpdateSummary();
        Save();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Erledigt-Haken kann den aktiven Filter betreffen.
        if (e.PropertyName == nameof(TodoItem.IsDone))
            view.Refresh();

        UpdateSummary();
        Save();
    }

    private void UpdateSummary()
    {
        var open = todos.Count(t => !t.IsDone);

        CountText.Text = todos.Count switch
        {
            0 => "Keine Aufgaben",
            _ => $"{open} von {todos.Count} offen"
        };

        ClearDoneButton.IsEnabled = todos.Count != open;
        EmptyText.Visibility = view.IsEmpty ? Visibility.Visible : Visibility.Collapsed;

        if (todos.Count > 0 && view.IsEmpty)
            EmptyText.Text = "Zu diesem Filter passt keine Aufgabe.";
        else
            EmptyText.Text = "Noch keine Aufgaben.\nOben eintippen und mit Enter hinzufügen.";
    }

    private void Save()
    {
        if (LocalStore.TrySave(FileName, todos) is { } error)
            status.SetStatus($"Aufgaben konnten nicht gespeichert werden: {error}", StatusKind.Error);
    }

    /// <summary>Ein Eintrag der Filter-Auswahl.</summary>
    public sealed class TodoFilter(string label, Func<TodoItem, bool> predicate)
    {
        public string Label { get; } = label;

        public bool Matches(TodoItem todo) => predicate(todo);

        public override string ToString() => Label;

        public static TodoFilter[] All { get; } =
        [
            new("Alle", _ => true),
            new("Offen", t => !t.IsDone),
            new("Erledigt", t => t.IsDone)
        ];
    }
}
