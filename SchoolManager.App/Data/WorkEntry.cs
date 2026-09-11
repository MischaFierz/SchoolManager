using System.Globalization;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Ein Leistungsdetail innerhalb einer Aufgabe: die eigentliche Zeitbuchung.
/// </summary>
public sealed class WorkEntry : WorkNode
{
    private int minutes;
    private DateTimeOffset date = DateTimeOffset.Now;

    /// <summary>Aufgewendete Zeit in Minuten.</summary>
    public int Minutes
    {
        get => minutes;
        set
        {
            if (!Set(ref minutes, Math.Max(0, value)))
                return;

            OnPropertyChanged(nameof(DurationText));
            RaiseTotals();
        }
    }

    /// <summary>Tag der Leistung.</summary>
    public DateTimeOffset Date
    {
        get => date;
        set
        {
            if (Set(ref date, value))
                OnPropertyChanged(nameof(DateText));
        }
    }

    /// <summary>
    /// Die Dauer als Text für das Eingabefeld. Versteht "90", "1:30", "1,5h";
    /// unverständliche Eingaben werden verworfen, das Feld springt zurück.
    /// </summary>
    [JsonIgnore]
    public string DurationText
    {
        get => TimeText.Format(Minutes);
        set
        {
            if (TimeText.TryParse(value, out var parsed))
                Minutes = parsed;
            else
                OnPropertyChanged(nameof(DurationText));
        }
    }

    /// <summary>Das Datum als Text für das Eingabefeld, Format tt.mm.jjjj.</summary>
    [JsonIgnore]
    public string DateText
    {
        get => Date.ToLocalTime().ToString("dd.MM.yyyy");
        set
        {
            if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("de-CH"),
                    DateTimeStyles.None, out var parsed))
                Date = new DateTimeOffset(parsed);
            else
                OnPropertyChanged(nameof(DateText));
        }
    }

    [JsonIgnore]
    public override int TotalMinutes => Minutes;

    [JsonIgnore]
    public override bool SupportsDueDate => false;

    [JsonIgnore]
    public override string LevelName => "Leistungsdetail";

    protected override string DefaultTitle => "Leistung ohne Titel";
}
