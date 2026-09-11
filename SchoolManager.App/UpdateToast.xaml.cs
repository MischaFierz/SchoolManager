using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace SchoolManager.App;

/// <summary>
/// Kleines, unaufdringliches Fenster unten rechts, das kurz auf ein verfügbares
/// Update hinweist. Schliesst sich nach ein paar Sekunden von selbst.
/// </summary>
public partial class UpdateToast : Window
{
    private const double ScreenEdgeGap = 20;
    private const int VisibleSeconds = 5;

    private readonly DispatcherTimer closeTimer = new() { Interval = TimeSpan.FromSeconds(VisibleSeconds) };

    /// <summary>Der Anwender hat auf den Hinweis geklickt und möchte zu den Einstellungen wechseln.</summary>
    public event Action? OpenSettingsRequested;

    public UpdateToast(string version)
    {
        InitializeComponent();

        MessageText.Text = $"Version {version} steht bereit.";

        Loaded += (_, _) => PositionBottomRight();

        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            FadeOutAndClose();
        };

        Opacity = 0;
        ContentRendered += (_, _) =>
        {
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
            closeTimer.Start();
        };
    }

    private void PositionBottomRight()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - ScreenEdgeGap;
        Top = area.Bottom - ActualHeight - ScreenEdgeGap;
    }

    private void FadeOutAndClose()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(250));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Card_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        closeTimer.Stop();
        OpenSettingsRequested?.Invoke();
        Close();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        closeTimer.Stop();
        Close();
    }
}
