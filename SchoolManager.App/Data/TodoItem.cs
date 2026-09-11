using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Eine Aufgabe auf der To-Do-Liste. Sie darf einem Fach zugeordnet sein und
/// ein Datum tragen, bis wann sie erledigt sein soll.
///
/// Dieses Datum bleibt bewusst aus dem Kalender heraus: Die To-Do-Liste ist der
/// Notizzettel neben dem Stundenplan, nicht Teil davon. Für die
/// Benachrichtigungen zählt es trotzdem mit.
/// </summary>
public sealed class TodoItem : INotifyPropertyChanged
{
    private string text = "";
    private string subject = "";
    private bool isDone;
    private DateTimeOffset? dueDate;

    public string Text
    {
        get => text;
        set => Set(ref text, value);
    }

    /// <summary>Fach, etwa Mathematik - darf leer bleiben.</summary>
    public string Subject
    {
        get => subject;
        set => Set(ref subject, value);
    }

    public bool IsDone
    {
        get => isDone;
        set
        {
            if (!Set(ref isDone, value))
                return;

            DoneAt = value ? DateTimeOffset.Now : null;
            OnPropertyChanged(nameof(DoneAt));
            OnPropertyChanged(nameof(IsOverdue));
            OnPropertyChanged(nameof(DueDescription));
        }
    }

    /// <summary>Bis wann die Aufgabe erledigt sein soll; darf offen bleiben.</summary>
    public DateTimeOffset? DueDate
    {
        get => dueDate;
        set
        {
            if (!Set(ref dueDate, value))
                return;

            OnPropertyChanged(nameof(DueText));
            OnPropertyChanged(nameof(DueDescription));
            OnPropertyChanged(nameof(IsOverdue));
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? DoneAt { get; set; }

    /// <summary>
    /// Das Datum als Text für das Eingabefeld; leer heisst: kein Termin. Eine
    /// unlesbare Eingabe wird zurückgesetzt, statt den Termin zu verlieren.
    /// </summary>
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

    /// <summary>Der Termin in Worten, etwa "heute fällig" oder "2 Tage überfällig".</summary>
    [JsonIgnore]
    public string DueDescription
    {
        get
        {
            if (DueDate is not { } due)
                return "";

            var days = (due.LocalDateTime.Date - DateTime.Today).Days;

            return days switch
            {
                0 => "heute fällig",
                1 => "morgen fällig",
                -1 when !IsDone => "seit gestern überfällig",
                < 0 when !IsDone => $"{-days} Tage überfällig",
                < 0 => $"war am {due.LocalDateTime:dd.MM.yyyy} fällig",
                _ => $"fällig am {due.LocalDateTime:dd.MM.yyyy}"
            };
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() => string.IsNullOrWhiteSpace(Subject)
        ? Text
        : $"{Subject}: {Text}";

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
