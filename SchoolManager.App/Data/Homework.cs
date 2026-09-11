using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Eine Hausaufgabe. Kann auf einen Auftrag, eine Aufgabe oder ein
/// Leistungsdetail verweisen - gespeichert wird dafür nur die Kennung.
/// </summary>
public sealed class Homework : INotifyPropertyChanged
{
    private string title = "";
    private string subject = "";
    private string notes = "";
    private bool isDone;
    private DateTimeOffset? dueDate;
    private string? linkId;

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    public string Title
    {
        get => title;
        set
        {
            if (Set(ref title, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    /// <summary>Fach, etwa Mathematik oder Informatik.</summary>
    public string Subject
    {
        get => subject;
        set
        {
            if (Set(ref subject, value))
                OnPropertyChanged(nameof(SubtitleText));
        }
    }

    public string Notes
    {
        get => notes;
        set => Set(ref notes, value);
    }

    public bool IsDone
    {
        get => isDone;
        set
        {
            if (!Set(ref isDone, value))
                return;

            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(IsOverdue));
        }
    }

    /// <summary>Abgabetermin; darf offen bleiben.</summary>
    public DateTimeOffset? DueDate
    {
        get => dueDate;
        set
        {
            if (!Set(ref dueDate, value))
                return;

            OnPropertyChanged(nameof(DueText));
            OnPropertyChanged(nameof(DueDescription));
            OnPropertyChanged(nameof(SubtitleText));
            OnPropertyChanged(nameof(IsOverdue));
        }
    }

    /// <summary>Kennung des verknüpften Auftrags, der Aufgabe oder des Leistungsdetails.</summary>
    public string? LinkId
    {
        get => linkId;
        set => Set(ref linkId, value);
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Hausaufgabe ohne Titel" : Title;

    /// <summary>Der Abgabetermin als Text für das Eingabefeld; leer heisst: kein Termin.</summary>
    [JsonIgnore]
    public string DueText
    {
        get => DueDate?.ToLocalTime().ToString("dd.MM.yyyy") ?? "";
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                DueDate = null;
                return;
            }

            if (DateTime.TryParse(value, CultureInfo.GetCultureInfo("de-CH"),
                    DateTimeStyles.None, out var parsed))
                DueDate = new DateTimeOffset(parsed.Date);
            else
                OnPropertyChanged(nameof(DueText));
        }
    }

    /// <summary>Termin überschritten und noch nicht erledigt.</summary>
    [JsonIgnore]
    public bool IsOverdue => !IsDone && DueDate is { } due && due.LocalDateTime.Date < DateTime.Today;

    /// <summary>Zweite Zeile in der Liste: Fach und Termin.</summary>
    [JsonIgnore]
    public string SubtitleText
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(Subject))
                parts.Add(Subject);

            parts.Add(DueDescription);

            if (IsDone)
                parts.Add("erledigt");

            return string.Join(" · ", parts);
        }
    }

    /// <summary>Der Termin in Worten, etwa "heute fällig" oder "2 Tage überfällig".</summary>
    [JsonIgnore]
    public string DueDescription
    {
        get
        {
            if (DueDate is not { } due)
                return "kein Termin";

            var days = (due.LocalDateTime.Date - DateTime.Today).Days;

            return days switch
            {
                0 => "heute fällig",
                1 => "morgen fällig",
                -1 => "seit gestern überfällig",
                < 0 => $"{-days} Tage überfällig",
                _ => $"fällig am {due.LocalDateTime:dd.MM.yyyy} (in {days} Tagen)"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => $"{DisplayTitle}, {SubtitleText}";

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
