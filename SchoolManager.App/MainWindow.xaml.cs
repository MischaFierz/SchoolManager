using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SchoolManager.App.Data;
using SchoolManager.App.Logging;
using SchoolManager.App.Notifications;
using SchoolManager.App.Pages;
using SchoolManager.App.Update;
using SchoolManager.Core;

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
    private readonly ReminderService reminders;

    /// <summary>Prüft von Zeit zu Zeit, ob etwas offen ist.</summary>
    private readonly DispatcherTimer reminderTimer = new() { Interval = TimeSpan.FromHours(1) };

    /// <summary>Symbol im Infobereich; nur vorhanden, solange Erinnerungen eingeschaltet sind.</summary>
    private TrayNotifier? tray;

    /// <summary>Wurde das Fenster ohne sichtbares Fenster gestartet (Autostart)?</summary>
    private readonly bool startedHidden;

    /// <summary>Beim Schliessen wirklich beenden - statt in den Infobereich zu gehen.</summary>
    private bool isExiting;

    /// <summary>Der Hinweis "läuft weiter im Infobereich" kommt nur einmal.</summary>
    private bool closeHintShown;

    /// <summary>So lange bleibt es nach einer Meldung still.</summary>
    private static readonly TimeSpan QuietPeriod = TimeSpan.FromHours(4);

    private readonly OrdersPage ordersPage;
    private readonly TasksPage tasksPage;
    private readonly HomeworkPage homeworkPage;
    private readonly ExamsPage examsPage;
    private readonly CalendarPage calendarPage;
    private readonly TeachersPage teachersPage;
    private readonly MailPage mailPage;
    private readonly TodoPage todoPage;
    private readonly NotesPage notesPage;
    private readonly LogPage logPage;
    private readonly SettingsPage settingsPage;

    public MainWindow() : this(false)
    {
    }

    /// <param name="startHidden">
    /// Beim Anmelden mitgestartet: Das Fenster bleibt zu, nur der Infobereich
    /// läuft mit und erinnert.
    /// </param>
    public MainWindow(bool startHidden)
    {
        startedHidden = startHidden;

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
        logPage = new LogPage(this);
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

        reminders = new ReminderService(workStore, homeworkStore, eventStore);

        ShowDevMode();
        DevMode.Changed += ShowDevMode;

        // Die Erinnerungen hängen nicht am Fenster: Beim Autostart wird es nie
        // angezeigt, und trotzdem muss gemeldet werden, was offen ist.
        reminderTimer.Tick += (_, _) => CheckReminders(force: false);
        NotificationSettings.Changed += ApplyNotificationSettings;
        ApplyNotificationSettings();

        // Kurz nach dem Start einmal nachsehen. Das läuft über einen Zeitgeber
        // und nicht über das geladene Fenster, weil es beim Autostart gar kein
        // geladenes Fenster gibt.
        var ersterBlick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };

        ersterBlick.Tick += (_, _) =>
        {
            ersterBlick.Stop();
            CheckReminders(force: false);
        };

        ersterBlick.Start();

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
        AskAboutNotifications();
    }

    /// <summary>
    /// Solange keine Anwendungs-ID eingebaut ist, führt die Anmeldung bei
    /// Microsoft 365 ins Leere und der Versand ist damit nicht fertig - das
    /// sagt ein Streifen oben gleich beim Start. Steckt eine ID darin, ist der
    /// Versand vollständig und der Streifen entfällt; dann genügt der Hinweis
    /// in der Fussleiste, falls noch kein Konto eingerichtet ist.
    /// </summary>
    private void ShowMailWarning()
    {
        if (SmtpSettings.HasBuiltInClientId)
        {
            MailWarningBanner.Visibility = Visibility.Collapsed;
            return;
        }

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

        NavLog.Visibility = DevMode.IsEnabled ? Visibility.Visible : Visibility.Collapsed;

        // Wer den Entwicklermodus abschaltet, während das Protokoll offen ist,
        // stünde sonst vor einer Seite, die es nicht mehr gibt.
        if (!DevMode.IsEnabled && NavLog.IsChecked == true)
            NavSettings.IsChecked = true;
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
        ShowUpdateBanner(update);
    }

    /// <summary>
    /// Zeigt den Hinweis auf ein Update oben im Fenster. Über ein Update wird
    /// immer informiert - anders als bei den Erinnerungen gibt es dafür keinen
    /// Schalter.
    /// </summary>
    private void ShowUpdateBanner(UpdateInfo update)
    {
        UpdateBannerText.Text = $"Version {update.Version} steht bereit.";
        UpdateBanner.Visibility = Visibility.Visible;
    }

    private void UpdateBannerSettings_Click(object sender, RoutedEventArgs e)
    {
        UpdateBanner.Visibility = Visibility.Collapsed;
        NavSettings.IsChecked = true;
    }

    private void UpdateBannerClose_Click(object sender, RoutedEventArgs e) =>
        UpdateBanner.Visibility = Visibility.Collapsed;

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

            case nameof(NavLog):
                PageHost.Content = logPage;
                logPage.Activate();
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
        // Die Fussleiste ist flüchtig - die nächste Meldung überschreibt sie.
        // Fehler wandern deshalb zusätzlich ins Protokoll, wo sie stehen
        // bleiben und im Entwicklermodus nachzulesen sind.
        if (kind == StatusKind.Error)
            AppLog.Error(text, "Oberfläche");

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

    // ==== Benachrichtigungen ====

    /// <summary>
    /// Fragt beim allerersten Start, ob erinnert werden soll. Voreingestellt
    /// ist Ja - wer die Frage einfach bestätigt, wird erinnert und findet den
    /// Schalter später in den Einstellungen.
    /// </summary>
    private void AskAboutNotifications()
    {
        if (NotificationSettings.WasAsked)
            return;

        var wanted = MessageBox.Show(
            this,
            "Soll School Manager an offene Sachen erinnern - überfällige Aufträge und Aufgaben, "
            + "Hausaufgaben, bevorstehende Prüfungen und To-Dos?\n\n"
            + "Die Erinnerungen kommen auch dann, wenn kein Fenster offen ist: School Manager "
            + "startet dafür beim Anmelden im Hintergrund mit und legt ein Symbol neben die Uhr.\n\n"
            + "Das lässt sich in den Einstellungen jederzeit ändern.",
            "Erinnerungen einschalten?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.Yes) == MessageBoxResult.Yes;

        NotificationSettings.MarkAsked();
        NotificationSettings.SetEnabled(wanted);
        NotificationSettings.SetAutoStart(wanted);

        // Stand die Einstellung schon auf dem gewünschten Wert, hat sich nichts
        // gemeldet - der Autostart muss trotzdem eingerichtet werden.
        ApplyNotificationSettings();

        SetStatus(
            wanted
                ? "Erinnerungen sind eingeschaltet."
                : "Erinnerungen bleiben aus - einschalten in den Einstellungen.",
            StatusKind.Info);
    }

    /// <summary>Richtet Symbol, Zeitgeber und Autostart nach der Einstellung aus.</summary>
    private void ApplyNotificationSettings()
    {
        if (NotificationSettings.Enabled)
        {
            tray ??= CreateTray();
            reminderTimer.Start();
        }
        else
        {
            reminderTimer.Stop();
            tray?.Dispose();
            tray = null;
        }

        AutoStartService.Apply();
    }

    private TrayNotifier CreateTray()
    {
        var notifier = new TrayNotifier();

        notifier.OpenRequested += ShowFromTray;
        notifier.ExitRequested += ExitApplication;

        return notifier;
    }

    /// <summary>
    /// Holt das Fenster aus dem Infobereich zurück nach vorne. Das ruft auch
    /// ein zweiter Programmstart auf, statt ein weiteres Fenster zu öffnen.
    /// </summary>
    public void ShowFromTray()
    {
        Show();

        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    /// <summary>
    /// Meldet, dass das Fenster diesmal wirklich zugehen darf, statt in den
    /// Infobereich auszuweichen.
    ///
    /// Das brauchen die Wege, die School Manager beenden, um sich selbst
    /// ersetzen zu lassen: Update einspielen, auf die öffentliche Version
    /// zurück, Daten einlesen, zurücksetzen, deinstallieren. Bei allen wartet
    /// ein Skript darauf, dass dieser Prozess endet - bliebe er im Infobereich
    /// stehen, wartete es ewig.
    /// </summary>
    public void PrepareForExit()
    {
        isExiting = true;

        reminderTimer.Stop();

        tray?.Dispose();
        tray = null;
    }

    /// <summary>
    /// Beendet School Manager wirklich - samt Symbol im Infobereich.
    ///
    /// Nach dem Autostart gab es nie ein sichtbares Fenster, und ein solches
    /// Fenster lässt sich auch nicht schliessen. Dann wird gleich hier
    /// gesichert und aufgeräumt, statt den Umweg über das Schliessen zu gehen.
    /// </summary>
    private void ExitApplication()
    {
        isExiting = true;

        if (IsVisible)
        {
            Close();
            return;
        }

        FlushPages();

        tray?.Dispose();
        tray = null;

        Application.Current.Shutdown();
    }

    /// <summary>
    /// Sieht nach, was offen ist, und meldet es über den Infobereich.
    /// <paramref name="force"/> übergeht die Schonfrist zwischen zwei
    /// Meldungen; das braucht die Schaltfläche in den Einstellungen.
    /// </summary>
    /// <returns>Wie viele offene Sachen gefunden wurden.</returns>
    public int CheckReminders(bool force)
    {
        if (!NotificationSettings.Enabled && !force)
            return 0;

        var open = reminders.Collect(NotificationSettings.LeadDays);

        if (open.Count == 0)
            return 0;

        // Nicht ständig dasselbe melden: zwischen zwei Meldungen liegen
        // mindestens ein paar Stunden.
        if (!force && NotificationSettings.LastReminded is { } last
                   && DateTimeOffset.Now - last < QuietPeriod)
            return open.Count;

        if (tray is { } notifier)
        {
            notifier.Show(TitleFor(open.Count), TextFor(open));
            NotificationSettings.MarkReminded();
        }

        return open.Count;
    }

    private static string TitleFor(int count) =>
        count == 1 ? "1 offene Sache" : $"{count} offene Sachen";

    /// <summary>Höchstens ein paar Zeilen - eine Sprechblase ist kein Bericht.</summary>
    private static string TextFor(IReadOnlyList<Reminder> open)
    {
        const int maxLines = 4;

        var lines = open.Take(maxLines).Select(item => item.Line).ToList();

        if (open.Count > maxLines)
            lines.Add($"… und {open.Count - maxLines} weitere");

        return string.Join(Environment.NewLine, lines);
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

    /// <summary>
    /// Beim Schliessen die Eingaben sichern - und, solange erinnert werden
    /// soll, nur das Fenster zumachen. School Manager läuft dann im
    /// Infobereich weiter, sonst kämen nach dem Schliessen keine Erinnerungen
    /// mehr. Beendet wird er dort über "Beenden".
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        FlushPages();

        if (!isExiting && tray is { } notifier && NotificationSettings.Enabled)
        {
            e.Cancel = true;
            Hide();

            if (!closeHintShown)
            {
                notifier.Show("School Manager läuft weiter",
                    "Das Symbol neben der Uhr erinnert an offene Sachen. "
                    + "Dort lässt sich School Manager auch ganz beenden.");

                closeHintShown = true;
            }

            return;
        }

        base.OnClosing(e);

        tray?.Dispose();
        tray = null;

        // Das Programm läuft mit ShutdownMode.OnExplicitShutdown; ohne diesen
        // Aufruf bliebe es nach dem letzten Fenster als Prozess zurück.
        Application.Current.Shutdown();
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
