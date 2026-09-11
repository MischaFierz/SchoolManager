using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SchoolManager.App.Data;
using SchoolManager.App.Pages;
using SchoolManager.App.Update;

namespace SchoolManager.App;

/// <summary>
/// Rahmen der Anwendung: Seitennavigation links, aktive Seite rechts und eine
/// gemeinsame Fussleiste für Statusmeldungen.
/// </summary>
public partial class MainWindow : Window, IStatusSink
{
    private readonly SmtpSettingsService settingsService = new();
    private readonly WorkStore workStore = new();
    private readonly HomeworkStore homeworkStore = new();
    private readonly TimetableStore timetableStore = new();
    private readonly LessonPlanStore lessonPlanStore = new();
    private readonly EventStore eventStore = new();
    private readonly TeacherStore teacherStore = new();

    private readonly CalendarFeed calendarFeed;

    private readonly OrdersPage ordersPage;
    private readonly TasksPage tasksPage;
    private readonly HomeworkPage homeworkPage;
    private readonly ExamsPage examsPage;
    private readonly CalendarPage calendarPage;
    private readonly TeachersPage teachersPage;
    private readonly MailPage mailPage;
    private readonly TodoPage todoPage;
    private readonly NotesPage notesPage;
    private readonly SettingsPage settingsPage;

    public MainWindow()
    {
        InitializeComponent();

        calendarFeed = new CalendarFeed(
            timetableStore, lessonPlanStore, eventStore, homeworkStore, workStore, teacherStore);

        ordersPage = new OrdersPage(workStore, calendarFeed, this);
        tasksPage = new TasksPage(workStore, calendarFeed, this);
        homeworkPage = new HomeworkPage(workStore, homeworkStore, this);
        examsPage = new ExamsPage(eventStore, this);
        calendarPage = new CalendarPage(
            timetableStore, lessonPlanStore, eventStore, homeworkStore, workStore,
            teacherStore, calendarFeed, this);
        teachersPage = new TeachersPage(teacherStore, this);
        mailPage = new MailPage(settingsService, teacherStore, this);
        todoPage = new TodoPage(this);
        notesPage = new NotesPage(this);
        settingsPage = new SettingsPage(settingsService, this);

        // Aus dem Auftrag heraus die Aufgabe öffnen.
        ordersPage.OpenTaskRequested += task =>
        {
            NavTasks.IsChecked = true;
            tasksPage.Select(task);
        };

        // Aus einer Hausaufgabe heraus den verknüpften Eintrag öffnen.
        homeworkPage.OpenNodeRequested += OpenNode;

        // Von der Prüfung in die passende Kalenderwoche.
        examsPage.ShowInCalendarRequested += date =>
        {
            NavCalendar.IsChecked = true;
            calendarPage.ShowWeekOf(date);
        };

        // Von der Lehrkraft direkt zur E-Mail.
        teachersPage.ComposeMailRequested += teacher =>
        {
            NavMail.IsChecked = true;
            mailPage.AddRecipient(teacher);
        };

        workStore.SaveFailed += error =>
            SetStatus($"Aufträge konnten nicht gespeichert werden: {error}", StatusKind.Error);
        homeworkStore.SaveFailed += error =>
            SetStatus($"Hausaufgaben konnten nicht gespeichert werden: {error}", StatusKind.Error);
        eventStore.SaveFailed += error =>
            SetStatus($"Prüfungen und Termine konnten nicht gespeichert werden: {error}", StatusKind.Error);
        teacherStore.SaveFailed += error =>
            SetStatus($"Lehrkräfte konnten nicht gespeichert werden: {error}", StatusKind.Error);
        lessonPlanStore.SaveFailed += error =>
            SetStatus($"Der Stundenplan konnte nicht gespeichert werden: {error}", StatusKind.Error);

        ShowDevMode();
        DevMode.Changed += ShowDevMode;

        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        NavOrders.IsChecked = true;

        // Der Hinweis auf die Einstellungen bleibt in der Fussleiste stehen,
        // solange der E-Mail-Versand noch nicht eingerichtet ist.
        SetStatus(
            settingsService.IsConfigured
                ? "Bereit"
                : "Für den E-Mail-Versand ist noch kein Konto eingerichtet - siehe Einstellungen.",
            StatusKind.Info);

        _ = CheckForUpdateOnStartupAsync();
        ShowMailWarning();
    }

    /// <summary>
    /// Der E-Mail-Versand ist in dieser Fassung noch nicht benutzbar, darum
    /// sagt es ein Streifen oben gleich beim Start. Er ist bewusst eine feste
    /// Aussage über diese Version und nicht das Ergebnis einer Prüfung: Die
    /// Seite E-Mail und die Einstellungen bleiben bedienbar, verlassen sollte
    /// man sich aber auf nichts davon.
    /// </summary>
    private void ShowMailWarning()
    {
        MailWarningText.Text =
            "Der E-Mail-Versand ist in dieser Version noch nicht verfügbar und wird "
            + "mit einem späteren Update nachgereicht.";

        MailWarningText.ToolTip =
            "Die Seite E-Mail und die Postausgangs-Einstellungen lassen sich zwar öffnen, "
            + "der Versand ist aber noch nicht einsatzbereit. Sobald er es ist, kommt er "
            + "über die eingebaute Update-Suche von selbst nach.";

        MailWarningBanner.Visibility = Visibility.Visible;
    }

    private void MailWarningSettings_Click(object sender, RoutedEventArgs e)
    {
        NavSettings.IsChecked = true;
        MailWarningBanner.Visibility = Visibility.Collapsed;
    }

    private void MailWarningClose_Click(object sender, RoutedEventArgs e) =>
        MailWarningBanner.Visibility = Visibility.Collapsed;

    // ==== Entwicklermodus ====

    /// <summary>Wie oft schon auf die Versionsnummer geklickt wurde.</summary>
    private int versionClicks;

    /// <summary>
    /// Siebenmal auf die Versionsnummer schaltet den Entwicklermodus frei -
    /// versehentlich findet das niemand, gesucht findet es jeder.
    /// </summary>
    private void VersionText_Click(object sender, MouseButtonEventArgs e)
    {
        if (DevMode.IsEnabled)
        {
            SetStatus("Der Entwicklermodus ist bereits aktiv - abschalten in den Einstellungen.",
                StatusKind.Info);
            return;
        }

        versionClicks++;

        var missing = DevMode.ClicksToUnlock - versionClicks;

        if (missing > 0)
        {
            // Erst kurz vor dem Ziel etwas sagen, sonst verrät es sich beim
            // ersten versehentlichen Klick.
            if (missing <= 3)
                SetStatus($"Noch {missing} …", StatusKind.Info);

            return;
        }

        versionClicks = 0;
        DevMode.Enable();
        ShowDevMode();

        NavSettings.IsChecked = true;
        SetStatus(
            DevMode.BackupPath is null
                ? "Entwicklermodus aktiv - siehe Einstellungen. Eine Sicherung der Daten kam nicht zustande."
                : "Entwicklermodus aktiv - die Daten sind gesichert; siehe Einstellungen.",
            StatusKind.Success);
    }

    /// <summary>Hängt den Hinweis an die Versionsnummer, solange der Modus läuft.</summary>
    private void ShowDevMode()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3);

        VersionText.Text = DevMode.IsEnabled
            ? $"Version {version} · Dev"
            : $"Version {version}";

        VersionText.Foreground = DevMode.IsEnabled
            ? (Brush)FindResource("KindAbgabe")
            : (Brush)FindResource("TextFaint");
    }

    /// <summary>
    /// Prüft still im Hintergrund auf ein Update; findet sich eines, erscheint kurz ein
    /// Hinweis unten rechts und die Einstellungsseite zeigt es schon vorausgefüllt an.
    /// </summary>
    private async Task CheckForUpdateOnStartupAsync()
    {
        UpdateInfo? update;

        try
        {
            update = await UpdateService.CheckForUpdateAsync();
        }
        catch
        {
            // Ein automatischer Hintergrund-Check soll beim Start nicht mit einem Fehler stören.
            return;
        }

        if (update is null)
            return;

        settingsPage.ShowAvailableUpdate(update);

        var toast = new UpdateToast(update.Version) { Owner = this };
        toast.OpenSettingsRequested += () => NavSettings.IsChecked = true;
        toast.Show();
    }

    // ==== Navigation ====

    private void Nav_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton nav)
            return;

        // Offene Änderungen beim Verlassen einer Seite sichern.
        FlushPages();

        switch (nav.Name)
        {
            case nameof(NavOrders):
                PageHost.Content = ordersPage;
                ordersPage.Activate();
                break;

            case nameof(NavTasks):
                PageHost.Content = tasksPage;
                tasksPage.Activate();
                break;

            case nameof(NavHomework):
                PageHost.Content = homeworkPage;
                homeworkPage.Activate();
                break;

            case nameof(NavExams):
                PageHost.Content = examsPage;
                examsPage.Activate();
                break;

            case nameof(NavCalendar):
                PageHost.Content = calendarPage;
                calendarPage.Activate();
                break;

            case nameof(NavTeachers):
                PageHost.Content = teachersPage;
                teachersPage.Activate();
                break;

            case nameof(NavMail):
                PageHost.Content = mailPage;
                mailPage.Activate();
                break;

            case nameof(NavTodo):
                PageHost.Content = todoPage;
                todoPage.Activate();
                break;

            case nameof(NavNotes):
                PageHost.Content = notesPage;
                notesPage.Activate();
                break;

            default:
                PageHost.Content = settingsPage;
                settingsPage.Activate();
                break;
        }
    }

    /// <summary>Öffnet einen Auftrag, eine Aufgabe oder ein Leistungsdetail.</summary>
    private void OpenNode(WorkNode node)
    {
        switch (node)
        {
            case WorkOrder order:
                NavOrders.IsChecked = true;
                ordersPage.Select(order);
                break;

            case WorkTask task:
                NavTasks.IsChecked = true;
                tasksPage.Select(task);
                break;

            case WorkEntry entry when entry.Parent is WorkTask parent:
                NavTasks.IsChecked = true;
                tasksPage.Select(parent);

                // Die Zeile des Leistungsdetails gleich aufklappen.
                entry.IsEditorOpen = true;
                break;
        }
    }

    // ==== Fussleiste ====

    public void SetStatus(string text, StatusKind kind)
    {
        StatusText.Text = text;
        StatusText.ToolTip = text.Length > 90 ? text : null;

        var brush = (Brush)FindResource(kind switch
        {
            StatusKind.Success => "Success",
            StatusKind.Error => "Error",
            _ => "TextMuted"
        });

        StatusDot.Background = brush;
        StatusText.Foreground = brush;
    }

    // ==== Tastenkuerzel ====

    protected override async void OnPreviewKeyDown(KeyEventArgs e)
    {
        base.OnPreviewKeyDown(e);

        if (Keyboard.Modifiers != ModifierKeys.Control)
            return;

        switch (e.Key)
        {
            // Strg+Enter verschickt die Nachricht, wenn die E-Mail-Seite offen ist.
            case Key.Enter when ReferenceEquals(PageHost.Content, mailPage):
                e.Handled = true;
                await mailPage.SendShortcutAsync();
                break;

            case Key.D1:
                e.Handled = true;
                NavOrders.IsChecked = true;
                break;

            case Key.D2:
                e.Handled = true;
                NavTasks.IsChecked = true;
                break;

            case Key.D3:
                e.Handled = true;
                NavHomework.IsChecked = true;
                break;

            case Key.D4:
                e.Handled = true;
                NavExams.IsChecked = true;
                break;

            case Key.D5:
                e.Handled = true;
                NavCalendar.IsChecked = true;
                break;

            case Key.D6:
                e.Handled = true;
                NavTeachers.IsChecked = true;
                break;

            case Key.D7:
                e.Handled = true;
                NavMail.IsChecked = true;
                break;

            case Key.D8:
                e.Handled = true;
                NavTodo.IsChecked = true;
                break;

            case Key.D9:
                e.Handled = true;
                NavNotes.IsChecked = true;
                break;

            case Key.D0:
                e.Handled = true;
                NavSettings.IsChecked = true;
                break;
        }
    }

    /// <summary>Beim Schliessen noch nicht gespeicherte Eingaben sichern.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        FlushPages();
        base.OnClosing(e);
    }

    /// <summary>Schreibt offene Änderungen der Seiten auf die Festplatte.</summary>
    private void FlushPages()
    {
        notesPage.Flush();
        workStore.Flush();
    }

    /// <summary>Setzt die dunkle Fensterleiste von Windows 11.</summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        DarkTitleBar.Apply(this);
    }
}
