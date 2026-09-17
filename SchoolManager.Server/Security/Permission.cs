namespace SchoolManager.Server.Security;

/// <summary>
/// Einzelne Rechte. Ein Benutzer hat die Rechte seiner Stufe, dazu die ihm
/// selbst gegebenen und die aller seiner Gruppen.
/// </summary>
[Flags]
public enum Permission
{
    None = 0,

    /// <summary>Mit diesem Konto in School Manager den Entwicklermodus öffnen.</summary>
    DevMode = 1 << 0,

    /// <summary>Jede Dev-Version beziehen, nicht nur die freigegebenen.</summary>
    AllDevUpdates = 1 << 1,

    /// <summary>Meldungen schreiben, die oben in der App erscheinen.</summary>
    Messages = 1 << 2,

    /// <summary>Den Hinweistext zu einer Version bearbeiten.</summary>
    UpdateNotes = 1 << 3,

    /// <summary>Dev-Versionen für Benutzer und Gruppen freigeben.</summary>
    ApproveUpdates = 1 << 4,

    /// <summary>Versionen veröffentlichen und Dev-Versionen aufräumen.</summary>
    Release = 1 << 5,

    /// <summary>Benutzer anlegen, ändern und löschen.</summary>
    Users = 1 << 6,

    /// <summary>Gruppen anlegen, ändern und löschen.</summary>
    Groups = 1 << 7,

    /// <summary>Den Verlauf aller Änderungen im Panel ansehen.</summary>
    Audit = 1 << 8,

    /// <summary>Server-Einstellungen wie den Entwicklungszweig ändern.</summary>
    Settings = 1 << 9,

    All = DevMode | AllDevUpdates | Messages | UpdateNotes | ApproveUpdates | Release | Users | Groups | Audit | Settings,

    /// <summary>Wer eines davon hat, kommt ins Panel.</summary>
    Panel = Messages | UpdateNotes | ApproveUpdates | Release | Users | Groups | Audit | Settings
}

/// <summary>Die Stufe eines Benutzers; sie bringt Grundrechte mit.</summary>
public enum AccessLevel
{
    /// <summary>Entwicklermodus, aber nur freigegebene Dev-Versionen.</summary>
    Tester = 0,

    /// <summary>Entwicklermodus mit allen Dev-Versionen.</summary>
    Entwickler = 1,

    /// <summary>Alles, auch die Verwaltung von Administratoren.</summary>
    Administrator = 2
}

public static class Permissions
{
    /// <summary>Beschriftungen für das Panel, in der Reihenfolge der Anzeige.</summary>
    public static IReadOnlyList<(Permission Value, string Label, string Hint)> Catalog { get; } =
    [
        (Permission.DevMode, "Entwicklermodus", "Mit diesem Konto in School Manager den Entwicklermodus öffnen"),
        (Permission.AllDevUpdates, "Alle Dev-Versionen", "Jede Dev-Version beziehen, nicht nur freigegebene"),
        (Permission.Messages, "Meldungen", "Meldungen schreiben, die oben in der App erscheinen"),
        (Permission.UpdateNotes, "Update-Infos", "Den Hinweistext zu einer Version bearbeiten"),
        (Permission.ApproveUpdates, "Freigaben", "Dev-Versionen für Benutzer und Gruppen freigeben"),
        (Permission.Release, "Releases", "Versionen veröffentlichen und Dev-Versionen aufräumen"),
        (Permission.Users, "Benutzer", "Benutzer anlegen, ändern und löschen"),
        (Permission.Groups, "Gruppen", "Gruppen anlegen, ändern und löschen"),
        (Permission.Audit, "Verlauf", "Den Verlauf aller Änderungen ansehen"),
        (Permission.Settings, "Einstellungen", "Server-Einstellungen ändern")
    ];

    public static Permission ForLevel(AccessLevel level) => level switch
    {
        AccessLevel.Administrator => Permission.All,
        AccessLevel.Entwickler => Permission.DevMode | Permission.AllDevUpdates,
        _ => Permission.DevMode
    };

    public static bool Has(this Permission granted, Permission wanted) => (granted & wanted) == wanted;

    public static bool HasAny(this Permission granted, Permission wanted) => (granted & wanted) != 0;

    public static string[] ToNames(Permission value) =>
        Catalog.Where(entry => value.Has(entry.Value)).Select(entry => entry.Value.ToString()).ToArray();

    /// <summary>Liest Namen aus dem Panel; Unbekanntes fällt weg.</summary>
    public static Permission FromNames(IEnumerable<string>? names)
    {
        var result = Permission.None;

        foreach (var name in names ?? [])
        {
            if (Enum.TryParse<Permission>(name, ignoreCase: false, out var value)
                && Catalog.Any(entry => entry.Value == value))
                result |= value;
        }

        return result;
    }
}
