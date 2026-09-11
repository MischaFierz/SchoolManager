using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Art eines selbst erfassten Kalendereintrags.</summary>
public enum CalendarEventKind
{
    /// <summary>Eine Prüfung.</summary>
    Pruefung,

    /// <summary>Ein sonstiger Termin.</summary>
    Termin
}

/// <summary>
/// Ein von Hand erfasster oder eingelesener Kalendereintrag - Prüfung oder
/// Termin. Erscheint im Kalender und lässt sich als ICS ausgeben.
/// </summary>
public sealed class CalendarEvent : INotifyPropertyChanged
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    private CalendarEventKind kind = CalendarEventKind.Pruefung;
    private string subject = "";
    private string title = "";
    private string room = "";
    private string notes = "";
    private DateTimeOffset start = new(DateTime.Today.AddDays(7).AddHours(8));
    private int durationMinutes = 45;
    private bool isAllDay;

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public CalendarEventKind Kind
    {
        get => kind;
        set
        {
            if (Set(ref kind, value))
                OnPropertyChanged(nameof(KindLabel));
        }
    }

    /// <summary>Fach, etwa Mathematik.</summary>
    public string Subject
    {
        get => subject;
        set
        {
            if (Set(ref subject, value))
                RaiseTexts();
        }
    }

    /// <summary>Thema oder Titel, etwa "Kapitel 3 bis 5".</summary>
    public string Title
    {
        get => title;
        set
        {
            if (Set(ref title, value))
                RaiseTexts();
        }
    }

    public string Room
    {
        get => room;
        set
        {
            if (Set(ref room, value))
                RaiseTexts();
        }
    }

    public string Notes
    {
        get => notes;
        set => Set(ref notes, value);
    }

    /// <summary>Beginn mit Datum und Uhrzeit.</summary>
    public DateTimeOffset Start
    {
        get => start;
        set
        {
            if (Set(ref start, value))
                RaiseTexts();
        }
    }

    /// <summary>Dauer in Minuten.</summary>
    public int DurationMinutes
    {
        get => durationMinutes;
        set
        {
            if (Set(ref durationMinutes, Math.Max(0, value)))
                RaiseTexts();
        }
    }

    /// <summary>Ganztägiger Eintrag - dann zählt nur das Datum.</summary>
    public bool IsAllDay
    {
        get => isAllDay;
        set
        {
            if (Set(ref isAllDay, value))
                RaiseTexts();
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public DateTime End => Start.LocalDateTime.AddMinutes(IsAllDay ? 0 : DurationMinutes);

    [JsonIgnore]
    public string KindLabel => Kind == CalendarEventKind.Pruefung ? "Prüfung" : "Termin";

    [JsonIgnore]
    public string DisplayTitle
    {
        get
        {
            var hasSubject = !string.IsNullOrWhiteSpace(Subject);
            var hasTitle = !string.IsNullOrWhiteSpace(Title);

            return (hasSubject, hasTitle) switch
            {
                (true, true) => $"{Subject}: {Title}",
                (true, false) => Subject,
                (false, true) => Title,
                _ => Kind == CalendarEventKind.Pruefung ? "Prüfung ohne Fach" : "Termin ohne Titel"
            };
        }
    }

    /// <summary>Zweite Zeile in der Liste: Zeitpunkt, Dauer und Raum.</summary>
    [JsonIgnore]
    public string SubtitleText
    {
        get
        {
            var parts = new List<string> { WhenText };

            if (!IsAllDay && DurationMinutes > 0)
                parts.Add($"{TimeText.Format(DurationMinutes)} h");

            if (!string.IsNullOrWhiteSpace(Room))
                parts.Add(Room);

            return string.Join(" · ", parts);
        }
    }

    [JsonIgnore]
    public string WhenText => IsAllDay
        ? $"{Start.LocalDateTime:ddd dd.MM.yyyy} ganztägig"
        : $"{Start.LocalDateTime:ddd dd.MM.yyyy} {Start.LocalDateTime:HH:mm}";

    /// <summary>Datum als Text für das Eingabefeld.</summary>
    [JsonIgnore]
    public string DateText
    {
        get => Start.LocalDateTime.ToString("dd.MM.yyyy");
        set
        {
            if (DateTime.TryParse(value, Culture, DateTimeStyles.None, out var parsed))
                Start = new DateTimeOffset(parsed.Date + Start.LocalDateTime.TimeOfDay);
            else
                OnPropertyChanged(nameof(DateText));
        }
    }

    /// <summary>Beginn als Text für das Eingabefeld, Format hh:mm.</summary>
    [JsonIgnore]
    public string StartTimeText
    {
        get => Start.LocalDateTime.ToString("HH:mm");
        set
        {
            if (TimeSpan.TryParse(value, Culture, out var parsed) && parsed < TimeSpan.FromDays(1))
                Start = new DateTimeOffset(Start.LocalDateTime.Date + parsed);
            else
                OnPropertyChanged(nameof(StartTimeText));
        }
    }

    /// <summary>Dauer als Text für das Eingabefeld.</summary>
    [JsonIgnore]
    public string DurationText
    {
        get => TimeText.Format(DurationMinutes);
        set
        {
            if (TimeText.TryParse(value, out var parsed))
                DurationMinutes = parsed;
            else
                OnPropertyChanged(nameof(DurationText));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => $"{KindLabel}: {DisplayTitle}, {WhenText}";

    private void RaiseTexts()
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(WhenText));
        OnPropertyChanged(nameof(DateText));
        OnPropertyChanged(nameof(StartTimeText));
        OnPropertyChanged(nameof(DurationText));
        OnPropertyChanged(nameof(End));
    }

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
}
