using System.Globalization;
using System.IO;
using System.Text;

namespace SchoolManager.App.Data;

/// <summary>
/// Schreibt Kalendereinträge als ICS-Datei, wie sie Outlook, Google Kalender
/// und Apple Kalender einlesen können.
/// </summary>
public static class IcsWriter
{
    private const string LineBreak = "\r\n";

    /// <summary>Baut den Inhalt einer ICS-Datei aus den Einträgen.</summary>
    public static string Build(IEnumerable<CalendarEntry> entries)
    {
        var builder = new StringBuilder();

        builder.Append("BEGIN:VCALENDAR").Append(LineBreak);
        builder.Append("VERSION:2.0").Append(LineBreak);
        builder.Append("PRODID:-//School Manager//Kalender//DE").Append(LineBreak);
        builder.Append("CALSCALE:GREGORIAN").Append(LineBreak);

        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

        foreach (var entry in entries)
        {
            builder.Append("BEGIN:VEVENT").Append(LineBreak);
            builder.Append("UID:").Append(entry.Id).Append("@schoolmanager").Append(LineBreak);
            builder.Append("DTSTAMP:").Append(stamp).Append(LineBreak);

            if (entry.IsAllDay)
            {
                builder.Append("DTSTART;VALUE=DATE:").Append(Date(entry.Start)).Append(LineBreak);
                builder.Append("DTEND;VALUE=DATE:").Append(Date(entry.Start.Date.AddDays(1))).Append(LineBreak);
            }
            else
            {
                builder.Append("DTSTART:").Append(Local(entry.Start)).Append(LineBreak);
                builder.Append("DTEND:").Append(Local(entry.End)).Append(LineBreak);
            }

            builder.Append("SUMMARY:").Append(Escape(Summary(entry))).Append(LineBreak);

            if (!string.IsNullOrWhiteSpace(entry.Subtitle))
                builder.Append("LOCATION:").Append(Escape(entry.Subtitle)).Append(LineBreak);

            if (!string.IsNullOrWhiteSpace(entry.Description))
                builder.Append("DESCRIPTION:").Append(Escape(entry.Description)).Append(LineBreak);

            builder.Append("END:VEVENT").Append(LineBreak);
        }

        builder.Append("END:VCALENDAR").Append(LineBreak);

        return builder.ToString();
    }

    /// <summary>Schreibt die Einträge in eine Datei und gibt deren Anzahl zurück.</summary>
    public static int Write(string path, IEnumerable<CalendarEntry> entries)
    {
        var list = entries.ToList();
        File.WriteAllText(path, Build(list), new UTF8Encoding(false));
        return list.Count;
    }

    /// <summary>Vor dem Titel steht die Art, damit man im fremden Kalender sieht, was es ist.</summary>
    private static string Summary(CalendarEntry entry) => entry.Kind switch
    {
        CalendarEntryKind.Lektion => entry.Title,
        _ => $"{entry.KindLabel}: {entry.Title}"
    };

    private static string Date(DateTime value) =>
        value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static string Local(DateTime value) =>
        value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);

    private static string Escape(string value) => value
        .Replace("\\", "\\\\")
        .Replace(";", "\\;")
        .Replace(",", "\\,")
        .Replace("\r\n", "\\n")
        .Replace("\n", "\\n");
}
