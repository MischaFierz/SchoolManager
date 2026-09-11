using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>
/// Gemeinsame Grundlage von Auftrag, Aufgabe und Leistungsdetail: Titel,
/// Beschreibung, Auftraggeber, Fach, Fertig-Markierung, die benötigte
/// Arbeitszeit sowie die erfasste Zeitsumme des Teilbaums.
/// </summary>
public abstract class WorkNode : INotifyPropertyChanged
{
    private string title = "";
    private string details = "";
    private string client = "";
    private string subject = "";
    private bool isDone;
    private DateTimeOffset? dueDate;
    private bool hasEstimate;
    private int estimateMinutes;
    private bool isSelected;
    private bool isEditorOpen;

    /// <summary>Bleibt über Umbenennungen stabil; darauf verweisen die Hausaufgaben.</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Titel des Eintrags.</summary>
    public string Title
    {
        get => title;
        set
        {
            if (Set(ref title, value))
                OnPropertyChanged(nameof(DisplayTitle));
        }
    }

    /// <summary>Freie Beschreibung - bei allen drei Ebenen vorhanden.</summary>
    public string Details
    {
        get => details;
        set => Set(ref details, value);
    }

    /// <summary>Auftraggeber; wird beim Anlegen von der übergeordneten Ebene übernommen.</summary>
    public string Client
    {
        get => client;
        set => Set(ref client, value);
    }

    /// <summary>Fach; wird beim Anlegen von der übergeordneten Ebene übernommen.</summary>
    public string Subject
    {
        get => subject;
        set => Set(ref subject, value);
    }

    /// <summary>
    /// Frühere Bezeichnung dieses Feldes („Firma“). Wird nur noch eingelesen,
    /// damit Dateien aus älteren Fassungen ihren Wert behalten; geschrieben
    /// wird ausschliesslich <see cref="Subject"/>.
    /// </summary>
    [JsonPropertyName("Company")]
    public string? LegacyCompany
    {
        get => null;
        set
        {
            if (!string.IsNullOrWhiteSpace(value) && string.IsNullOrWhiteSpace(subject))
                subject = value;
        }
    }

    /// <summary>Fertig-Markierung; auf jeder Ebene einzeln setzbar.</summary>
    public bool IsDone
    {
        get => isDone;
        set
        {
            if (Set(ref isDone, value))
                RaiseDoneChanged();
        }
    }

    /// <summary>
    /// Enddatum: Auftrag und Aufgabe haben eines, Leistungsdetails nicht.
    /// Steht es, erscheint der Eintrag als Abgabe im Kalender.
    /// </summary>
    public DateTimeOffset? DueDate
    {
        get => dueDate;
        set
        {
            if (!Set(ref dueDate, value))
                return;

            OnPropertyChanged(nameof(DueText));
            OnPropertyChanged(nameof(DueDescription));
        }
    }

    /// <summary>Leistungsdetails kennen kein Enddatum.</summary>
    [JsonIgnore]
    public virtual bool SupportsDueDate => true;

    /// <summary>Das Enddatum als Text für das Eingabefeld; leer heisst: keines.</summary>
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

            if (DateTime.TryParse(value, System.Globalization.CultureInfo.GetCultureInfo("de-CH"),
                    System.Globalization.DateTimeStyles.None, out var parsed))
                DueDate = new DateTimeOffset(parsed.Date);
            else
                OnPropertyChanged(nameof(DueText));
        }
    }

    /// <summary>Das Enddatum in Worten.</summary>
    [JsonIgnore]
    public string DueDescription
    {
        get
        {
            if (DueDate is not { } due)
                return "kein Enddatum";

            var days = (due.LocalDateTime.Date - DateTime.Today).Days;

            return days switch
            {
                0 => "heute fällig",
                1 => "morgen fällig",
                -1 => "seit gestern überfällig",
                < 0 when !IsDone => $"{-days} Tage überfällig",
                < 0 => $"war am {due.LocalDateTime:dd.MM.yyyy} fällig",
                _ => $"fällig am {due.LocalDateTime:dd.MM.yyyy} (in {days} Tagen)"
            };
        }
    }

    /// <summary>Ist eine benötigte Arbeitszeit angegeben? Schaltet das Eingabefeld frei.</summary>
    public bool HasEstimate
    {
        get => hasEstimate;
        set
        {
            if (!Set(ref hasEstimate, value))
                return;

            OnPropertyChanged(nameof(EstimateText));
            OnPropertyChanged(nameof(EstimateSummary));
        }
    }

    /// <summary>Benötigte Arbeitszeit in Minuten; gilt nur mit <see cref="HasEstimate"/>.</summary>
    public int EstimateMinutes
    {
        get => estimateMinutes;
        set
        {
            if (!Set(ref estimateMinutes, Math.Max(0, value)))
                return;

            OnPropertyChanged(nameof(EstimateText));
            OnPropertyChanged(nameof(EstimateSummary));
        }
    }

    /// <summary>Die benötigte Zeit als Text für das Eingabefeld.</summary>
    [JsonIgnore]
    public string EstimateText
    {
        get => TimeText.Format(EstimateMinutes);
        set
        {
            if (TimeText.TryParse(value, out var parsed))
                EstimateMinutes = parsed;
            else
                OnPropertyChanged(nameof(EstimateText));
        }
    }

    /// <summary>Erfasste Zeit, bei Bedarf im Vergleich zur benötigten.</summary>
    [JsonIgnore]
    public string EstimateSummary => HasEstimate
        ? $"{TotalText} h von {TimeText.Format(EstimateMinutes)} h benötigt"
        : $"{TotalText} h erfasst";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    /// <summary>Übergeordneter Eintrag; wird beim Einfügen und beim Laden gesetzt.</summary>
    [JsonIgnore]
    public WorkNode? Parent { get; internal set; }

    /// <summary>Ausgewählt - nur Oberfläche, wird nicht gespeichert.</summary>
    [JsonIgnore]
    public bool IsSelected
    {
        get => isSelected;
        set => SetQuiet(ref isSelected, value);
    }

    /// <summary>Zeile in einer Liste aufgeklappt - nur Oberfläche.</summary>
    [JsonIgnore]
    public bool IsEditorOpen
    {
        get => isEditorOpen;
        set => SetQuiet(ref isEditorOpen, value);
    }

    /// <summary>Summe der Minuten dieses Eintrags samt allem darunter.</summary>
    [JsonIgnore]
    public abstract int TotalMinutes { get; }

    [JsonIgnore]
    public string TotalText => TimeText.Format(TotalMinutes);

    [JsonIgnore]
    public string TotalTextLong => TimeText.FormatBoth(TotalMinutes);

    [JsonIgnore]
    public string DisplayTitle => string.IsNullOrWhiteSpace(Title) ? DefaultTitle : Title;

    /// <summary>Kurzer Stand für Listen, etwa "2 von 3 erledigt".</summary>
    [JsonIgnore]
    public virtual string DoneSummary => IsDone ? "erledigt" : "offen";

    /// <summary>Bezeichnung der Ebene, etwa für Platzhalter und Rückfragen.</summary>
    [JsonIgnore]
    public abstract string LevelName { get; }

    protected abstract string DefaultTitle { get; }

    /// <summary>Wird bei jeder Änderung im Teilbaum ausgelöst - Anlass zum Speichern.</summary>
    public event Action? Changed;

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>Meldet, dass sich die Zeitsumme geändert hat - bis nach oben durch.</summary>
    internal void RaiseTotals()
    {
        OnPropertyChanged(nameof(TotalMinutes));
        OnPropertyChanged(nameof(TotalText));
        OnPropertyChanged(nameof(TotalTextLong));
        OnPropertyChanged(nameof(EstimateSummary));
        Parent?.RaiseTotals();
    }

    /// <summary>Meldet eine geänderte Fertig-Markierung - bis nach oben durch.</summary>
    internal void RaiseDoneChanged()
    {
        OnPropertyChanged(nameof(DoneSummary));
        Parent?.RaiseDoneChanged();
    }

    /// <summary>Meldet eine inhaltliche Änderung - bis nach oben durch.</summary>
    internal void RaiseChanged()
    {
        Changed?.Invoke();
        Parent?.RaiseChanged();
    }

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(property);
        RaiseChanged();
        return true;
    }

    /// <summary>Wie <see cref="Set{T}"/>, löst aber kein Speichern aus (Oberflächen-Zustand).</summary>
    private bool SetQuiet<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;

        field = value;
        OnPropertyChanged(property);
        return true;
    }

    protected void OnPropertyChanged(string? property) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

    /// <summary>
    /// Bedienhilfen und Screenreader lesen den Eintrag über ToString; ohne das
    /// würden sie in Listen den Klassennamen vorlesen.
    /// </summary>
    public override string ToString() => $"{LevelName}: {DisplayTitle}, {TotalText} h";

    /// <summary>Übernimmt Auftraggeber und Fach von der übergeordneten Ebene.</summary>
    public void InheritFromParent()
    {
        if (Parent is null)
            return;

        Client = Parent.Client;
        Subject = Parent.Subject;
    }

    /// <summary>Der Weg von oben herab, etwa "Website-Relaunch › Konzept › Wireframes".</summary>
    [JsonIgnore]
    public string Path => Parent is null ? DisplayTitle : $"{Parent.Path} › {DisplayTitle}";
}
