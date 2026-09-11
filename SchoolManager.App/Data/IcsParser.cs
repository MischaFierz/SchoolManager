using System.Globalization;
using System.Text;

namespace SchoolManager.App.Data;

/// <summary>Wie oft sich ein Termin wiederholt.</summary>
public enum IcsFrequency
{
    None,
    Daily,
    Weekly,
    Monthly,
    Yearly
}

/// <summary>Ein Termin aus einer ICS-Datei, gegebenenfalls mit Wiederholung.</summary>
public sealed class IcsEvent
{
    public string Summary { get; set; } = "";
    public string Location { get; set; } = "";
    public string Description { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public bool IsAllDay { get; set; }

    public IcsFrequency Frequency { get; set; } = IcsFrequency.None;
    public int Interval { get; set; } = 1;
    public DateTime? Until { get; set; }
    public int? Count { get; set; }

    /// <summary>Wochentage bei FREQ=WEEKLY; leer heisst: Wochentag von <see cref="Start"/>.</summary>
    public List<DayOfWeek> ByDays { get; } = [];

    /// <summary>Einzelne ausgenommene Termine (EXDATE).</summary>
    public List<DateTime> Exceptions { get; } = [];

    public TimeSpan Duration => End - Start;
}

/// <summary>Ein einzelner Eintrag im Stundenplan - eine konkrete Lektion.</summary>
public sealed class Lesson
{
    public required string Subject { get; init; }
    public string Location { get; init; } = "";
    public string Description { get; init; } = "";
    public DateTime Start { get; init; }
    public DateTime End { get; init; }
    public bool IsAllDay { get; init; }

    /// <summary>Name der ICS-Datei, aus der die Lektion stammt.</summary>
    public string Source { get; init; } = "";

    public string TimeText => IsAllDay ? "ganztägig" : $"{Start:HH:mm}–{End:HH:mm}";

    public override string ToString() => $"{Subject}, {TimeText}";
}

/// <summary>
/// Liest ICS-Dateien (iCalendar) so weit ein, wie es für einen Stundenplan
/// nötig ist: Termine mit Zeit, Ort und wöchentlicher Wiederholung.
/// </summary>
public static class IcsParser
{
    public static List<IcsEvent> Parse(string content)
    {
        var events = new List<IcsEvent>();
        IcsEvent? current = null;

        foreach (var line in Unfold(content))
        {
            if (line.Equals("BEGIN:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                current = new IcsEvent();
                continue;
            }

            if (line.Equals("END:VEVENT", StringComparison.OrdinalIgnoreCase))
            {
                if (current is not null && current.Start != default)
                {
                    // Termine ohne Ende dauern eine Lektion lang.
                    if (current.End <= current.Start)
                        current.End = current.Start.AddMinutes(45);

                    events.Add(current);
                }

                current = null;
                continue;
            }

            if (current is null)
                continue;

            var separator = line.IndexOf(':');

            if (separator <= 0)
                continue;

            var name = line[..separator];
            var value = line[(separator + 1)..];
            var parameters = "";

            if (name.IndexOf(';') is var semicolon && semicolon > 0)
            {
                parameters = name[(semicolon + 1)..];
                name = name[..semicolon];
            }

            switch (name.ToUpperInvariant())
            {
                case "SUMMARY":
                    current.Summary = Unescape(value);
                    break;

                case "LOCATION":
                    current.Location = Unescape(value);
                    break;

                case "DESCRIPTION":
                    current.Description = Unescape(value);
                    break;

                case "DTSTART":
                    if (TryParseDate(value, parameters, out var start, out var allDay))
                    {
                        current.Start = start;
                        current.IsAllDay = allDay;
                    }

                    break;

                case "DTEND":
                    if (TryParseDate(value, parameters, out var end, out _))
                        current.End = end;

                    break;

                case "DURATION":
                    if (TryParseDuration(value, out var duration) && current.Start != default)
                        current.End = current.Start + duration;

                    break;

                case "RRULE":
                    ApplyRule(current, value);
                    break;

                case "EXDATE":
                    foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries))
                        if (TryParseDate(part, parameters, out var exception, out _))
                            current.Exceptions.Add(exception);

                    break;
            }
        }

        return events;
    }

    /// <summary>
    /// Rechnet die Wiederholungen aus und gibt alle Lektionen zurück, die im
    /// Zeitraum liegen (<paramref name="to"/> ausschliesslich).
    /// </summary>
    public static IEnumerable<Lesson> Expand(
        IEnumerable<IcsEvent> events, DateTime from, DateTime to, string source = "")
    {
        foreach (var item in events)
        {
            foreach (var start in Occurrences(item, from, to))
            {
                if (item.Exceptions.Any(x => x.Date == start.Date && (item.IsAllDay || x.TimeOfDay == start.TimeOfDay)))
                    continue;

                yield return new Lesson
                {
                    Subject = string.IsNullOrWhiteSpace(item.Summary) ? "Ohne Titel" : item.Summary,
                    Location = item.Location,
                    Description = item.Description,
                    Start = start,
                    End = start + item.Duration,
                    IsAllDay = item.IsAllDay,
                    Source = source
                };
            }
        }
    }

    private static IEnumerable<DateTime> Occurrences(IcsEvent item, DateTime from, DateTime to)
    {
        if (item.Frequency == IcsFrequency.None)
        {
            if (item.Start < to && item.End > from)
                yield return item.Start;

            yield break;
        }

        var interval = Math.Max(1, item.Interval);
        var produced = 0;
        var limit = item.Until ?? to.AddYears(1);

        // Wochentermine: alle gewünschten Wochentage je Woche durchgehen.
        if (item.Frequency == IcsFrequency.Weekly)
        {
            var days = item.ByDays.Count > 0 ? item.ByDays : [item.Start.DayOfWeek];
            var weekStart = StartOfWeek(item.Start);

            for (var week = 0; ; week++)
            {
                var cursor = weekStart.AddDays(7 * interval * week);

                if (cursor > limit || cursor > to)
                    yield break;

                foreach (var day in days.OrderBy(d => ((int)d + 6) % 7))
                {
                    var offset = ((int)day + 6) % 7;
                    var occurrence = cursor.AddDays(offset) + item.Start.TimeOfDay;

                    if (occurrence < item.Start || occurrence > limit)
                        continue;

                    if (item.Count is { } max && produced >= max)
                        yield break;

                    produced++;

                    if (occurrence >= from && occurrence < to)
                        yield return occurrence;
                }
            }
        }

        var step = item.Frequency switch
        {
            IcsFrequency.Daily => (Func<DateTime, int, DateTime>)((d, i) => d.AddDays(i)),
            IcsFrequency.Monthly => (d, i) => d.AddMonths(i),
            _ => (d, i) => d.AddYears(i)
        };

        for (var occurrence = item.Start; occurrence <= limit; occurrence = step(occurrence, interval))
        {
            if (occurrence >= to)
                yield break;

            if (item.Count is { } max && produced >= max)
                yield break;

            produced++;

            if (occurrence >= from)
                yield return occurrence;
        }
    }

    public static DateTime StartOfWeek(DateTime value) =>
        value.Date.AddDays(-(((int)value.DayOfWeek + 6) % 7));

    private static void ApplyRule(IcsEvent item, string rule)
    {
        foreach (var part in rule.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var pair = part.Split('=', 2);

            if (pair.Length != 2)
                continue;

            switch (pair[0].ToUpperInvariant())
            {
                case "FREQ":
                    item.Frequency = pair[1].ToUpperInvariant() switch
                    {
                        "DAILY" => IcsFrequency.Daily,
                        "WEEKLY" => IcsFrequency.Weekly,
                        "MONTHLY" => IcsFrequency.Monthly,
                        "YEARLY" => IcsFrequency.Yearly,
                        _ => IcsFrequency.None
                    };

                    break;

                case "INTERVAL":
                    if (int.TryParse(pair[1], out var interval))
                        item.Interval = interval;

                    break;

                case "COUNT":
                    if (int.TryParse(pair[1], out var count))
                        item.Count = count;

                    break;

                case "UNTIL":
                    if (TryParseDate(pair[1], "", out var until, out _))
                        item.Until = until;

                    break;

                case "BYDAY":
                    foreach (var day in pair[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                    {
                        // Ein Vorzeichen wie "-1SU" wird hier nicht ausgewertet.
                        var code = day.TrimStart('+', '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9');

                        var parsed = code.ToUpperInvariant() switch
                        {
                            "MO" => DayOfWeek.Monday,
                            "TU" => DayOfWeek.Tuesday,
                            "WE" => DayOfWeek.Wednesday,
                            "TH" => DayOfWeek.Thursday,
                            "FR" => DayOfWeek.Friday,
                            "SA" => DayOfWeek.Saturday,
                            "SU" => DayOfWeek.Sunday,
                            _ => (DayOfWeek?)null
                        };

                        if (parsed is { } value && !item.ByDays.Contains(value))
                            item.ByDays.Add(value);
                    }

                    break;
            }
        }
    }

    private static bool TryParseDate(string value, string parameters, out DateTime result, out bool allDay)
    {
        result = default;
        allDay = parameters.Contains("VALUE=DATE", StringComparison.OrdinalIgnoreCase) || value.Length == 8;

        var text = value.Trim();
        var utc = text.EndsWith('Z');

        if (utc)
            text = text[..^1];

        var formats = new[] { "yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm", "yyyyMMdd" };

        if (!DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var parsed))
            return false;

        // Zeiten in UTC auf die lokale Zeit umrechnen; TZID-Angaben werden als
        // Ortszeit genommen - für einen Stundenplan ist das die Absicht.
        result = utc
            ? DateTime.SpecifyKind(parsed, DateTimeKind.Utc).ToLocalTime()
            : parsed;

        return true;
    }

    private static bool TryParseDuration(string value, out TimeSpan duration)
    {
        duration = TimeSpan.Zero;

        // Form PT1H30M / P1DT2H
        var text = value.Trim().ToUpperInvariant();

        if (!text.StartsWith('P'))
            return false;

        var number = new StringBuilder();
        var time = false;

        foreach (var c in text[1..])
        {
            if (char.IsDigit(c))
            {
                number.Append(c);
                continue;
            }

            if (c == 'T')
            {
                time = true;
                continue;
            }

            if (!int.TryParse(number.ToString(), out var amount))
                return false;

            number.Clear();

            duration += c switch
            {
                'W' => TimeSpan.FromDays(7 * amount),
                'D' => TimeSpan.FromDays(amount),
                'H' => TimeSpan.FromHours(amount),
                'M' when time => TimeSpan.FromMinutes(amount),
                'S' => TimeSpan.FromSeconds(amount),
                _ => TimeSpan.Zero
            };
        }

        return duration > TimeSpan.Zero;
    }

    /// <summary>Setzt umgebrochene Zeilen wieder zusammen (Fortsetzung beginnt mit Leerzeichen).</summary>
    private static IEnumerable<string> Unfold(string content)
    {
        var builder = new StringBuilder();

        foreach (var raw in content.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            if (line.Length > 0 && (line[0] == ' ' || line[0] == '\t'))
            {
                builder.Append(line[1..]);
                continue;
            }

            if (builder.Length > 0)
                yield return builder.ToString();

            builder.Clear();
            builder.Append(line);
        }

        if (builder.Length > 0)
            yield return builder.ToString();
    }

    private static string Unescape(string value) => value
        .Replace("\\n", "\n")
        .Replace("\\N", "\n")
        .Replace("\\,", ",")
        .Replace("\\;", ";")
        .Replace("\\\\", "\\");
}
