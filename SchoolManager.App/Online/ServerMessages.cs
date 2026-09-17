using System.IO;
using System.Text.Json;
using SchoolManager.App.Data;
using SchoolManager.App.Logging;

namespace SchoolManager.App.Online;

/// <summary>
/// Holt die Meldungen aus dem Admin-Panel und merkt sich, welche weggeklickt
/// wurden. Gemerkt wird Meldung und Stand: Wird eine Meldung im Panel
/// geändert, erscheint sie wieder.
/// </summary>
public static class ServerMessages
{
    private static readonly string FilePath = LocalStore.PathFor("meldungen.json");

    private static HashSet<string> dismissed = Load();

    /// <summary>Damit ein nicht erreichbarer Server das Protokoll nicht alle zehn Minuten füllt.</summary>
    private static bool unreachableLogged;

    /// <summary>Die Meldungen, die gerade erscheinen sollen; leer, wenn der Server nicht antwortet.</summary>
    public static async Task<IReadOnlyList<ServerMessage>> VisibleAsync()
    {
        if (!ServerApi.IsConfigured)
            return [];

        IReadOnlyList<ServerMessage> messages;

        try
        {
            messages = await ServerApi.MessagesAsync(DevMode.Token);
            unreachableLogged = false;
        }
        catch (ServerException ex)
        {
            if (!ex.IsUnreachable || !unreachableLogged)
                AppLog.Error($"Meldungen konnten nicht abgerufen werden: {ex.Message}", "Server");

            unreachableLogged |= ex.IsUnreachable;
            return [];
        }

        // Weggeklicktes, das es nicht mehr gibt, braucht niemand mehr zu merken.
        var keys = messages.Select(m => m.Key).ToHashSet();

        if (dismissed.RemoveWhere(key => !keys.Contains(key)) > 0)
            Save();

        return messages.Where(m => !dismissed.Contains(m.Key)).ToList();
    }

    public static void Dismiss(ServerMessage message)
    {
        if (dismissed.Add(message.Key))
            Save();
    }

    private static HashSet<string> Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(LocalStore.Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(dismissed));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dann erscheint die Meldung beim nächsten Start eben wieder.
        }
    }
}
