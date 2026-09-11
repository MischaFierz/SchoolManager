using System.IO;
using System.Drawing;
using System.Windows;
using Forms = System.Windows.Forms;

namespace SchoolManager.App.Notifications;

/// <summary>
/// Das Symbol im Infobereich neben der Uhr und die Sprechblasen, die von dort
/// aufsteigen.
///
/// Darüber laufen alle Erinnerungen: Sie sollen auch dann ankommen, wenn kein
/// Fenster offen ist - und genau dafür ist der Infobereich in Windows da.
/// Windows legt solche Meldungen zusätzlich in die Infozentrale, sie gehen also
/// nicht verloren, wenn gerade niemand hinsieht.
/// </summary>
public sealed class TrayNotifier : IDisposable
{
    private readonly Forms.NotifyIcon icon;

    /// <summary>Das Fenster soll in den Vordergrund - Doppelklick oder Menü.</summary>
    public event Action? OpenRequested;

    /// <summary>School Manager soll wirklich beendet werden.</summary>
    public event Action? ExitRequested;

    public TrayNotifier()
    {
        var menu = new Forms.ContextMenuStrip();

        menu.Items.Add("School Manager öffnen", null, (_, _) => OpenRequested?.Invoke());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Beenden", null, (_, _) => ExitRequested?.Invoke());

        icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "School Manager",
            Visible = true,
            ContextMenuStrip = menu
        };

        icon.DoubleClick += (_, _) => OpenRequested?.Invoke();
        icon.BalloonTipClicked += (_, _) => OpenRequested?.Invoke();
    }

    /// <summary>Zeigt eine Sprechblase; ohne Text zeigt Windows gar nichts an.</summary>
    public void Show(string title, string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return;

        icon.ShowBalloonTip(20_000, title, text, Forms.ToolTipIcon.Info);
    }

    /// <summary>Das Programmsymbol; im Notfall tut es auch das von Windows.</summary>
    private static Icon LoadIcon()
    {
        try
        {
            var resource = Application.GetResourceStream(new Uri("app.ico", UriKind.Relative));

            return resource is null ? SystemIcons.Application : new Icon(resource.Stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return SystemIcons.Application;
        }
    }

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
    }
}
