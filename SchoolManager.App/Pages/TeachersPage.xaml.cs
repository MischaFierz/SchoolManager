using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SchoolManager.App.Data;

namespace SchoolManager.App.Pages;

/// <summary>
/// Lehrkräfte mit Adresse und Fach. Die Liste lässt sich als CSV-Datei
/// ausgeben und wieder einlesen; auf der E-Mail-Seite dient sie als
/// Empfängerauswahl.
/// </summary>
public partial class TeachersPage : UserControl
{
    private readonly TeacherStore store;
    private readonly IStatusSink status;

    public TeachersPage(TeacherStore store, IStatusSink status)
    {
        this.store = store;
        this.status = status;

        InitializeComponent();

        TeacherList.ItemsSource = store.Items;
        store.Changed += UpdateSummary;

        if (store.Items.Count > 0)
            TeacherList.SelectedIndex = 0;

        UpdateSummary();
    }

    /// <summary>Bittet die Seitennavigation, eine E-Mail an diese Lehrkraft zu beginnen.</summary>
    public event Action<Teacher>? ComposeMailRequested;

    /// <summary>Wird von der Seitennavigation aufgerufen, wenn die Seite erscheint.</summary>
    public void Activate()
    {
        UpdateSummary();
        NewNameBox.Focus();
    }

    private Teacher? Selected => TeacherList.SelectedItem as Teacher;

    // ==== Anlegen und Löschen ====

    private void NewNameBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        e.Handled = true;
        Add();
    }

    private void Add_Click(object sender, RoutedEventArgs e) => Add();

    private void Add()
    {
        var name = NewNameBox.Text.Trim();

        if (name.Length == 0)
        {
            status.SetStatus("Bitte zuerst einen Namen eingeben.", StatusKind.Error);
            NewNameBox.Focus();
            return;
        }

        var teacher = store.Add(name);
        NewNameBox.Clear();

        TeacherList.SelectedItem = teacher;
        TeacherList.ScrollIntoView(teacher);

        status.SetStatus($"{teacher.DisplayName} hinzugefügt - jetzt die E-Mail-Adresse eintragen.",
            StatusKind.Success);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } teacher)
            return;

        if (MessageBox.Show(Window.GetWindow(this)!, $"„{teacher.DisplayName}“ löschen?",
                "Lehrkraft löschen", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes)
            return;

        store.Remove(teacher);
        status.SetStatus("Lehrkraft gelöscht.", StatusKind.Info);
        UpdateSummary();
    }

    private void Compose_Click(object sender, RoutedEventArgs e)
    {
        if (Selected is not { } teacher)
            return;

        if (!teacher.HasEmail)
        {
            status.SetStatus("Für diese Lehrkraft ist keine E-Mail-Adresse hinterlegt.", StatusKind.Error);
            return;
        }

        ComposeMailRequested?.Invoke(teacher);
    }

    // ==== Aus- und Einlesen ====

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (store.Items.Count == 0)
        {
            status.SetStatus("Die Liste ist leer - es gibt nichts zu exportieren.", StatusKind.Error);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Title = "Lehrkräfte exportieren",
            Filter = "CSV-Datei (*.csv)|*.csv",
            FileName = "Lehrkraefte.csv",
            AddExtension = true,
            DefaultExt = ".csv"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            store.ExportCsv(dialog.FileName);
            status.SetStatus(
                $"{store.Items.Count} Lehrkräfte nach {Path.GetFileName(dialog.FileName)} geschrieben.",
                StatusKind.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            status.SetStatus($"Speichern fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Lehrkräfte importieren",
            Filter = "CSV-Datei (*.csv)|*.csv|Alle Dateien (*.*)|*.*"
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) != true)
            return;

        try
        {
            var (added, updated) = store.ImportCsv(dialog.FileName);

            status.SetStatus(
                $"Eingelesen: {added} neu, {updated} aktualisiert.",
                StatusKind.Success);

            if (store.Items.Count > 0 && Selected is null)
                TeacherList.SelectedIndex = 0;

            UpdateSummary();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException
                                       or UnauthorizedAccessException)
        {
            status.SetStatus($"Einlesen fehlgeschlagen: {ex.Message}", StatusKind.Error);
        }
    }

    // ==== Oberfläche ====

    private void TeacherList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        DetailScroller.ScrollToTop();
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var withMail = store.Items.Count(teacher => teacher.HasEmail);

        SummaryText.Text = store.Items.Count switch
        {
            0 => "Keine Lehrkräfte",
            1 => $"1 Lehrkraft · {withMail} mit E-Mail-Adresse",
            _ => $"{store.Items.Count} Lehrkräfte · {withMail} mit E-Mail-Adresse"
        };

        var hasSelection = Selected is not null;

        DeleteButton.IsEnabled = hasSelection;
        ComposeButton.IsEnabled = Selected?.HasEmail == true;
        DetailScroller.Visibility = hasSelection ? Visibility.Visible : Visibility.Hidden;
        NoSelectionText.Visibility = hasSelection ? Visibility.Collapsed : Visibility.Visible;
        EmptyListText.Visibility = store.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}
