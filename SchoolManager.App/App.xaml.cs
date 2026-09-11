using System.Windows;
using SchoolManager.App.Notifications;

namespace SchoolManager.App;

/// <summary>
/// Startpunkt der Anwendung.
///
/// Normal öffnet sich das Fenster. Mit <c>--hintergrund</c> - so trägt der
/// Autostart School Manager beim Anmelden ein - bleibt es zu, und nur das
/// Symbol im Infobereich läuft mit und erinnert an Offenes.
///
/// Weil damit zwei Wege in dieselben Dateien führen, der Autostart und der
/// Doppelklick auf die Verknüpfung, achtet der Start darauf, dass es bei einer
/// Anwendung bleibt: Eine zweite holt die laufende nach vorne und beendet sich
/// selbst. Sonst überschrieben sich zwei Fenster gegenseitig die Daten.
/// </summary>
public partial class App : Application
{
    private const string InstanceName = @"Local\SchoolManager.Instanz";
    private const string ShowSignalName = @"Local\SchoolManager.Fenster";

    /// <summary>Hält fest, dass diese Anwendung läuft; darf nicht eingesammelt werden.</summary>
    private static Mutex? instanceLock;

    /// <summary>Darüber bittet ein zweiter Start die laufende Anwendung nach vorne.</summary>
    private static EventWaitHandle? showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var background = e.Args.Any(argument =>
            string.Equals(argument, AutoStartService.BackgroundArgument, StringComparison.OrdinalIgnoreCase));

        instanceLock = new Mutex(initiallyOwned: true, InstanceName, out var isFirst);

        if (!isFirst)
        {
            ShowRunningInstance(background);
            Shutdown();
            return;
        }

        // Im Hintergrund gestartet, aber niemand will erinnert werden: Dann
        // gibt es nichts zu tun, und ein Programm ohne Fenster und ohne
        // Aufgabe soll nicht im Speicher stehen bleiben.
        if (background && !NotificationSettings.Enabled)
        {
            Shutdown();
            return;
        }

        // Das Fenster darf zugehen, ohne dass die Erinnerungen aufhören -
        // beendet wird über das Symbol im Infobereich.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var window = new MainWindow(background);

        MainWindow = window;

        if (!background)
            window.Show();

        ListenForSecondStart(window);
    }

    /// <summary>
    /// Bittet die bereits laufende Anwendung, ihr Fenster zu zeigen. Kommt
    /// dieser Start selbst nur aus dem Autostart, bleibt alles, wie es ist.
    /// </summary>
    private static void ShowRunningInstance(bool background)
    {
        if (background)
            return;

        if (!EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
            return;

        using (signal)
            signal.Set();
    }

    /// <summary>
    /// Wartet darauf, dass ein zweiter Start das Fenster anfordert - etwa ein
    /// Doppelklick auf die Verknüpfung, während School Manager schon im
    /// Infobereich läuft.
    /// </summary>
    private static void ListenForSecondStart(MainWindow window)
    {
        showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);

        ThreadPool.RegisterWaitForSingleObject(
            showSignal,
            (_, _) => window.Dispatcher.Invoke(window.ShowFromTray),
            state: null,
            millisecondsTimeOutInterval: Timeout.Infinite,
            executeOnlyOnce: false);
    }
}
