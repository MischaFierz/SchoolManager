namespace SchoolManager.Core;

/// <summary>
/// Liefert ein Zugriffstoken für die OAuth2-Anmeldung am Postausgang.
/// Die Anmeldung selbst gehört in die Anwendung, nicht in diesen Kern.
/// </summary>
public interface IAccessTokenSource
{
    /// <summary>
    /// Gibt ein gültiges Token für das Postfach zurück. Darf ein Anmeldefenster
    /// öffnen, wenn kein gültiges Token vorliegt.
    /// </summary>
    Task<string> GetAccessTokenAsync(string userName, CancellationToken cancellationToken = default);
}
