using SchoolManager.Server.Data;

namespace SchoolManager.Server.Services;

/// <summary>Hält fest, wer im Panel was geändert hat.</summary>
public sealed class AuditLog(ServerDb db, TimeProvider clock)
{
    /// <summary>Merkt den Eintrag vor; gespeichert wird er mit dem nächsten SaveChanges.</summary>
    public void Add(string actor, string action) =>
        db.AuditEntries.Add(new AuditEntry
        {
            Time = clock.GetUtcNow(),
            Actor = actor,
            Action = action.Length > 500 ? action[..500] : action
        });
}
