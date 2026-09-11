using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace SchoolManager.App;

/// <summary>Setzt die dunkle Fensterleiste von Windows 11.</summary>
public static class DarkTitleBar
{
    private const int DwmwaUseImmersiveDarkMode = 20;

    public static void Apply(Window window)
    {
        if (PresentationSource.FromVisual(window) is not HwndSource source)
            return;

        var useDarkMode = 1;
        _ = DwmSetWindowAttribute(source.Handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
}
