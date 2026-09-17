using SchoolManager.Server.Security;

namespace SchoolManager.Server.Data;

public sealed class User
{
    public int Id { get; set; }

    /// <summary>Anmeldename, klein geschrieben gespeichert.</summary>
    public string UserName { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public string PasswordHash { get; set; } = "";

    public AccessLevel Level { get; set; }

    /// <summary>Rechte, die dieser Benutzer zusätzlich zu Stufe und Gruppen hat.</summary>
    public Permission ExtraPermissions { get; set; }

    /// <summary>Nur öffentliche Versionen - schlägt jede Freigabe und jedes Recht.</summary>
    public bool PublicOnly { get; set; }

    public bool IsActive { get; set; } = true;

    /// <summary>Beim nächsten Anmelden muss ein neues Passwort gesetzt werden.</summary>
    public bool MustChangePassword { get; set; }

    public int FailedLogins { get; set; }

    public DateTimeOffset? LockedUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? LastLoginAt { get; set; }

    public List<Group> Groups { get; set; } = [];

    public List<Session> Sessions { get; set; } = [];
}

public sealed class Group
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public string Description { get; set; } = "";

    public Permission Permissions { get; set; }

    public List<User> Users { get; set; } = [];
}

public enum SessionKind
{
    /// <summary>Angemeldet aus School Manager, für den Entwicklermodus.</summary>
    App = 0,

    /// <summary>Angemeldet im Admin-Panel.</summary>
    Panel = 1
}

public sealed class Session
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>SHA-256 des Tokens; das Token selbst kennt nur der Inhaber.</summary>
    public string TokenHash { get; set; } = "";

    public SessionKind Kind { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }
}

public enum MessageKind
{
    /// <summary>Roter Streifen.</summary>
    Error = 0,

    /// <summary>Grauer Hinweis.</summary>
    Info = 1
}

public enum MessageAudience
{
    Everyone = 0,

    /// <summary>Nur wer in School Manager als Entwickler angemeldet ist.</summary>
    Developers = 1
}

/// <summary>Wo eine Meldung erscheint; mehrere Orte zugleich sind möglich.</summary>
[Flags]
public enum MessagePlacement
{
    None = 0,

    /// <summary>Oben im Fenster von School Manager.</summary>
    App = 1 << 0,

    /// <summary>Oben auf der Startseite des Servers.</summary>
    StartPage = 1 << 1,

    /// <summary>Oben auf der Anmeldeseite.</summary>
    SignInPage = 1 << 2
}

public sealed class Message
{
    public int Id { get; set; }

    public string Text { get; set; } = "";

    public MessageKind Kind { get; set; }

    /// <summary>Wer sie in der App sieht. Auf den Webseiten sieht sie jeder.</summary>
    public MessageAudience Audience { get; set; }

    public MessagePlacement Placement { get; set; } = MessagePlacement.App;

    public bool IsActive { get; set; } = true;

    /// <summary>Danach erscheint die Meldung nicht mehr; leer heisst: bis sie abgeschaltet wird.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = "";
}

/// <summary>Der kurze Hinweistext, den die App zu einer Version anzeigt.</summary>
public sealed class ReleaseNote
{
    /// <summary>Der Tag, etwa v1.2.0 oder v1.2.1-dev.</summary>
    public string Tag { get; set; } = "";

    public string Text { get; set; } = "";

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = "";
}

/// <summary>Wer eine Dev-Version beziehen darf, ohne das Recht auf alle zu haben.</summary>
public sealed class DevApproval
{
    public string Tag { get; set; } = "";

    /// <summary>Jeder, der den Entwicklermodus nutzen darf.</summary>
    public bool ForAllDevelopers { get; set; }

    public List<User> Users { get; set; } = [];

    public List<Group> Groups { get; set; } = [];

    public DateTimeOffset UpdatedAt { get; set; }

    public string UpdatedBy { get; set; } = "";
}

public sealed class AuditEntry
{
    public int Id { get; set; }

    public DateTimeOffset Time { get; set; }

    public string Actor { get; set; } = "";

    public string Action { get; set; } = "";
}

/// <summary>Einstellungen, die sich im Panel ändern lassen, ohne den Server neu aufzusetzen.</summary>
public sealed class ServerSetting
{
    public string Key { get; set; } = "";

    public string Value { get; set; } = "";
}
