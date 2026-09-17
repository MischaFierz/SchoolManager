using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SchoolManager.Server.Data;
using SchoolManager.Server.GitHub;

namespace SchoolManager.Server.Services;

/// <summary>Einstellungen aus der Datenbank, mit Anfangswerten aus der Konfiguration.</summary>
public sealed class ServerSettings(ServerDb db, IOptions<GitHubOptions> github)
{
    private const string DevBranchKey = "DevBranch";

    /// <summary>Der Zweig im privaten Repository, von dem Dev-Versionen getaggt werden.</summary>
    public async Task<string> DevBranchAsync() =>
        (await db.Settings.FirstOrDefaultAsync(s => s.Key == DevBranchKey))?.Value ?? github.Value.DevBranch;

    public async Task SetDevBranchAsync(string branch)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Key == DevBranchKey);

        if (setting is null)
            db.Settings.Add(new ServerSetting { Key = DevBranchKey, Value = branch });
        else
            setting.Value = branch;
    }
}
