using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Eine Notiz mit Titel und freiem Text.</summary>
public sealed class Note : INotifyPropertyChanged
{
    private string title = "";
    private string subject = "";
    private string body = "";
    private DateTimeOffset updatedAt = DateTimeOffset.Now;

    public string Title
    {
        get => title;
        set => Set(ref title, value);
    }

    /// <summary>Fach, etwa Mathematik - darf leer bleiben.</summary>
    public string Subject
    {
        get => subject;
        set => Set(ref subject, value);
    }

    public string Body
    {
        get => body;
        set => Set(ref body, value);
    }

    public DateTimeOffset UpdatedAt
    {
        get => updatedAt;
        set => Set(ref updatedAt, value);
    }

    /// <summary>Titel für die Liste; leere Notizen bekommen einen Platzhalter.</summary>
    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? "Ohne Titel" : Title;

    /// <summary>Zweite Zeile der Liste: Fach, Zeitpunkt und Anfang des Textes.</summary>
    [JsonIgnore]
    public string Preview
    {
        get
        {
            var stamp = UpdatedAt.ToLocalTime().ToString("dd.MM.yyyy HH:mm");

            if (!string.IsNullOrWhiteSpace(Subject))
                stamp = $"{Subject} · {stamp}";
            var firstLine = Body.Replace("\r", " ").Replace("\n", " ").Trim();

            if (firstLine.Length == 0)
                return stamp;

            if (firstLine.Length > 60)
                firstLine = firstLine[..60] + "…";

            return $"{stamp} · {firstLine}";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return;

        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        // Titel und Vorschau in der Liste hängen von Titel, Text und Zeitpunkt ab.
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayTitle)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
}
