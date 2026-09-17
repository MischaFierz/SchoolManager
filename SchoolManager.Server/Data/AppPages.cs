namespace SchoolManager.Server.Data;

/// <summary>
/// Die Seiten von School Manager, auf die sich eine Meldung beschränken lässt.
/// Die Schlüssel müssen zu denen in der App passen (MainWindow, PageKeys).
/// </summary>
public static class AppPages
{
    /// <summary>Leer: auf jeder Seite der App.</summary>
    public const string Everywhere = "";

    public static IReadOnlyList<(string Key, string Label)> All { get; } =
    [
        (Everywhere, "Überall in der App"),
        ("orders", "Aufträge"),
        ("tasks", "Aufgaben"),
        ("homework", "Hausaufgaben"),
        ("exams", "Prüfungen"),
        ("calendar", "Kalender"),
        ("teachers", "Lehrkräfte"),
        ("mail", "E-Mail"),
        ("todo", "To-Do"),
        ("notes", "Notizen"),
        ("log", "Protokoll (nur Entwicklermodus)"),
        ("settings", "Einstellungen")
    ];

    public static bool IsKnown(string? key) => All.Any(page => page.Key == (key ?? ""));
}
