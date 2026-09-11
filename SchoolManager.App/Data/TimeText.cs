using System.Globalization;

namespace SchoolManager.App.Data;

/// <summary>
/// Rechnet Zeitangaben um: liest verschiedene Schreibweisen ein und gibt sie
/// einheitlich als Stunden:Minuten sowie als Dezimalstunden zurück.
/// </summary>
public static class TimeText
{
    /// <summary>
    /// Versteht "90" (Minuten), "1:30", "1,5h", "1.5 h", "2h", "45m" und "1h30".
    /// Ein leerer Text ergibt 0 Minuten.
    /// </summary>
    public static bool TryParse(string? text, out int minutes)
    {
        minutes = 0;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var value = text.Trim().ToLowerInvariant().Replace(',', '.').Replace(" ", "");

        // 1:30
        if (value.Contains(':'))
        {
            var parts = value.Split(':');

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var h) &&
                int.TryParse(parts[1], out var m) &&
                h >= 0 && m is >= 0 and < 60)
            {
                minutes = h * 60 + m;
                return true;
            }

            return false;
        }

        // 1h30 oder 1h30m
        var hIndex = value.IndexOf('h');

        if (hIndex > 0)
        {
            var head = value[..hIndex];
            var tail = value[(hIndex + 1)..].TrimEnd('m', 'i', 'n');

            if (!double.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var hours) || hours < 0)
                return false;

            var tailMinutes = 0;

            if (tail.Length > 0 && (!int.TryParse(tail, out tailMinutes) || tailMinutes is < 0 or >= 60))
                return false;

            minutes = (int)Math.Round(hours * 60) + tailMinutes;
            return true;
        }

        // 45m / 45min
        if (value.EndsWith('m') || value.EndsWith("min"))
        {
            var head = value.TrimEnd('m', 'i', 'n');
            return int.TryParse(head, out minutes) && minutes >= 0;
        }

        // Reine Zahl = Minuten
        return int.TryParse(value, out minutes) && minutes >= 0;
    }

    /// <summary>Ergibt "12:45".</summary>
    public static string Format(int minutes)
    {
        var sign = minutes < 0 ? "-" : "";
        minutes = Math.Abs(minutes);

        return $"{sign}{minutes / 60}:{minutes % 60:00}";
    }

    /// <summary>Ergibt "12,75" - die übliche Form für die Verrechnung.</summary>
    public static string FormatDecimalHours(int minutes) =>
        (minutes / 60.0).ToString("0.00", CultureInfo.GetCultureInfo("de-CH"));

    /// <summary>Ergibt "12:45 h (12,75)".</summary>
    public static string FormatBoth(int minutes) =>
        $"{Format(minutes)} h ({FormatDecimalHours(minutes)})";
}
