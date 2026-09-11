using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Notizen mit Liste links und Editor rechts. Änderungen werden kurz nach der
/// letzten Eingabe automatisch gespeichert.
/// </summary>
public partial class NotesPage : UserControl
{
    private const string FileName = "notes.json";

    private readonly ObservableCollection<Note> notes;
    private readonly DispatcherTimer saveTimer;
    private readonly IStatusSink status;

    /// <summary>Verhindert, dass das Setzen von UpdatedAt erneut als Änderung zählt.</summary>
    private bool suppressChanges;

    private Note? pendingNote;

    public NotesPage(IStatusSink status)
    {
        this.status = status;

        InitializeComponent();

        notes = new ObservableCollection<Note>(
            LocalStore.Load<Note>(FileName).OrderByDescending(n => n.UpdatedAt));

        foreach (var note in notes)
            note.PropertyChanged += Note_PropertyChanged;

        notes.CollectionChanged += Notes_CollectionChanged;
        NotesList.ItemsSource = notes;

        saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
        saveTimer.Tick += (_, _) => Flush();

        if (notes.Count > 0)
            NotesList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        if (NotesList.SelectedItem is not null)
            BodyBox.Focus();
    }

    private void NewNote_Click(object sender, RoutedEventArgs e)
    {
        // Offene Änderungen der vorherigen Notiz zuerst sichern.
        Flush();

        var note = new Note { Title = "Neue Notiz" };
        notes.Insert(0, note);
        NotesList.SelectedItem = note;

        TitleBox.Focus();
        TitleBox.SelectAll();
        status.SetStatus("Neue Notiz angelegt.", StatusKind.Success);
    }

    private void DeleteNote_Click(object sender, RoutedEventArgs e)
    {
        if (NotesList.SelectedItem is not Note note)
            return;

        var answer = MessageBox.Show(
            Window.GetWindow(this)!,
            $"Notiz \"{note.DisplayTitle}\" wirklich löschen?",
            "Notiz löschen",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (answer != MessageBoxResult.Yes)
            return;

        var index = notes.IndexOf(note);
        notes.Remove(note);

        if (notes.Count > 0)
            NotesList.SelectedIndex = Math.Min(index, notes.Count - 1);

        status.SetStatus("Notiz gelöscht.", StatusKind.Info);
    }

    private void NotesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Beim Wechsel die zuletzt bearbeitete Notiz sofort sichern.
        Flush();

        var hasSelection = NotesList.SelectedItem is Note;

        DeleteNoteButton.IsEnabled = hasSelection;
        EditorHeader.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        BodyBox.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Notes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var note in e.OldItems?.OfType<Note>() ?? [])
            note.PropertyChanged -= Note_PropertyChanged;

        foreach (var note in e.NewItems?.OfType<Note>() ?? [])
            note.PropertyChanged += Note_PropertyChanged;

        UpdateSummary();
        SaveNow();
    }

    private void Note_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (suppressChanges || sender is not Note note)
            return;

        if (e.PropertyName is not (nameof(Note.Title) or nameof(Note.Subject) or nameof(Note.Body)))
            return;

        pendingNote = note;
        SaveHintText.Text = "Nicht gespeicherte Änderungen…";

        // Timer neu starten: gespeichert wird kurz nach der letzten Eingabe.
        saveTimer.Stop();
        saveTimer.Start();
    }

    /// <summary>Schreibt offene Änderungen sofort auf die Festplatte.</summary>
    public void Flush()
    {
        saveTimer.Stop();

        if (pendingNote is null)
            return;

        suppressChanges = true;
        pendingNote.UpdatedAt = DateTimeOffset.Now;
        suppressChanges = false;

        pendingNote = null;
        SaveNow();
        UpdateSummary();
    }

    private void SaveNow()
    {
        if (LocalStore.TrySave(FileName, notes) is { } error)
        {
            SaveHintText.Text = "Speichern fehlgeschlagen.";
            status.SetStatus($"Notizen konnten nicht gespeichert werden: {error}", StatusKind.Error);
            return;
        }

        SaveHintText.Text = notes.Count == 0
            ? "Änderungen werden automatisch gespeichert."
            : $"Gespeichert um {DateTime.Now:HH:mm:ss}";
    }

    private void UpdateSummary()
    {
        CountText.Text = notes.Count switch
        {
            0 => "Keine Notizen",
            1 => "1 Notiz",
            _ => $"{notes.Count} Notizen"
        };

        EmptyListText.Visibility = notes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
