namespace SchoolManager.App;

/// <summary>Art einer Statusmeldung; bestimmt die Farbe in der Fussleiste.</summary>
public enum StatusKind
{
    Info,
    Success,
    Error
}

/// <summary>
/// Wird vom Hauptfenster bereitgestellt, damit die einzelnen Seiten Meldungen
/// in der gemeinsamen Fussleiste anzeigen können.
/// </summary>
public interface IStatusSink
{
    void SetStatus(string text, StatusKind kind);
}
