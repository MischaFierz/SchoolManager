using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Eine selbst erfasste Lektion im Stundenplan - entweder jede Woche am selben
/// Wochentag oder einmalig an einem Datum.
/// </summary>
public sealed class TimetableEntry : INotifyPropertyChanged
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    private string subject = "";
    private string room = "";
    private string note = "";
    private string? teacherId;
    private bool isWeekly = true;
    private DayOfWeek day = DayOfWeek.Monday;
    private DateTimeOffset date = new(DateTime.Today);
    private int startMinutes = 8 * 60 + 15;
    private int durationMinutes = 45;

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Fach, etwa Mathematik.</summary>
    public string Subject
    {
        get => subject;
        set
        {
            if (Set(ref subject, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    public string Room
    {
        get => room;
        set => Set(ref room, value);
    }

    public string Note
    {
        get => note;
        set => Set(ref note, value);
    }

    /// <summary>Kennung der Lehrkraft; leer heisst: über das Fach zuordnen.</summary>
    public string? TeacherId
    {
        get => teacherId;
        set => Set(ref teacherId, value);
    }

    /// <summary>Wiederholt sich jede Woche am <see cref="Day"/>.</summary>
    public bool IsWeekly
    {
        get => isWeekly;
        set
        {
            if (Set(ref isWeekly, value))
                OnPropertyChanged(nameof(WhenText));
        }
    }

    public DayOfWeek Day
    {
        get => day;
        set
        {
            if (Set(ref day, value))
                OnPropertyChanged(nameof(WhenText));
        }
    }

    /// <summary>Datum, wenn die Lektion nur einmal stattfindet.</summary>
    public DateTimeOffset Date
    {
        get => date;
        set
        {
            if (!Set(ref date, value))
                return;

            OnPropertyChanged(nameof(DateText));
            OnPropertyChanged(nameof(WhenText));
        }
    }

    /// <summary>Beginn in Minuten nach Mitternacht.</summary>
    public int StartMinutes
    {
        get => startMinutes;
        set
        {
            if (!Set(ref startMinutes, Math.Clamp(value, 0, 24 * 60 - 1)))
                return;

            OnPropertyChanged(nameof(StartTimeText));
            OnPropertyChanged(nameof(WhenText));
        }
    }

    public int DurationMinutes
    {
        get => durationMinutes;
        set
        {
            if (!Set(ref durationMinutes, Math.Max(5, value)))
                return;

            OnPropertyChanged(nameof(DurationText));
            OnPropertyChanged(nameof(WhenText));
        }
    }

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Subject) ? "Lektion ohne Fach" : Subject;

    /// <summary>Beginn als Text für das Eingabefeld, Format hh:mm.</summary>
    [JsonIgnore]
    public string StartTimeText
    {
        get => TimeSpan.FromMinutes(StartMinutes).ToString(@"hh\:mm");
        set
        {
            if (TimeSpan.TryParse(value, Culture, out var parsed) && parsed < TimeSpan.FromDays(1))
                StartMinutes = (int)parsed.TotalMinutes;
            else
                OnPropertyChanged(nameof(StartTimeText));
        }
    }

    [JsonIgnore]
    public string DurationText
    {
        get => TimeText.Format(DurationMinutes);
        set
        {
            if (TimeText.TryParse(value, out var parsed) && parsed > 0)
                DurationMinutes = parsed;
            else
                OnPropertyChanged(nameof(DurationText));
        }
    }

    [JsonIgnore]
    public string DateText
    {
        get => Date.LocalDateTime.ToString("dd.MM.yyyy");
        set
        {
            if (DateTime.TryParse(value, Culture, DateTimeStyles.None, out var parsed))
                Date = new DateTimeOffset(parsed.Date);
            else
                OnPropertyChanged(nameof(DateText));
        }
    }

    /// <summary>Wann die Lektion stattfindet, in Worten.</summary>
    [JsonIgnore]
    public string WhenText => IsWeekly
        ? $"jeden {DayName} {StartTimeText}–{EndTimeText}"
        : $"{Date.LocalDateTime:dd.MM.yyyy} {StartTimeText}–{EndTimeText}";

    [JsonIgnore]
    public string DayName => Culture.DateTimeFormat.GetDayName(Day);

    [JsonIgnore]
    public string EndTimeText =>
        TimeSpan.FromMinutes(StartMinutes + DurationMinutes).ToString(@"hh\:mm");

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Gilt die Lektion an diesem Tag?</summary>
    public bool AppliesTo(DateTime date) => IsWeekly
        ? date.DayOfWeek == Day
        : date.Date == Date.LocalDateTime.Date;

    /// <summary>Beginn an einem bestimmten Tag.</summary>
    public DateTime StartOn(DateTime date) => date.Date.AddMinutes(StartMinutes);

    public override string ToString() => $"{DisplayTitle}, {WhenText}";

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(property);
        return true;
    }

    private void OnPropertyChanged(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>Kopie zum Bearbeiten, damit „Abbrechen“ nichts verändert.</summary>
    public TimetableEntry Clone() => new()
    {
        Id = Id,
        Subject = Subject,
        Room = Room,
        Note = Note,
        TeacherId = TeacherId,
        IsWeekly = IsWeekly,
        Day = Day,
        Date = Date,
        StartMinutes = StartMinutes,
        DurationMinutes = DurationMinutes
    };

    /// <summary>Übernimmt die Werte einer Kopie.</summary>
    public void CopyFrom(TimetableEntry other)
    {
        Subject = other.Subject;
        Room = other.Room;
        Note = other.Note;
        TeacherId = other.TeacherId;
        IsWeekly = other.IsWeekly;
        Day = other.Day;
        Date = other.Date;
        StartMinutes = other.StartMinutes;
        DurationMinutes = other.DurationMinutes;
    }
}
