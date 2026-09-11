using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Eine Lehrkraft mit E-Mail-Adresse - Vorlage für die Empfängerauswahl.</summary>
public sealed class Teacher : INotifyPropertyChanged
{
    private string name = "";
    private string shortName = "";
    private string subject = "";
    private string email = "";
    private string phone = "";
    private string room = "";
    private string notes = "";

    public string Id { get; set; } = Guid.NewGuid().ToString("n");

    /// <summary>Voller Name, etwa "Frau Meier".</summary>
    public string Name
    {
        get => name;
        set
        {
            if (Set(ref name, value))
                RaiseTexts();
        }
    }

    /// <summary>Kürzel, wie es im Stundenplan steht.</summary>
    public string ShortName
    {
        get => shortName;
        set
        {
            if (Set(ref shortName, value))
                RaiseTexts();
        }
    }

    public string Subject
    {
        get => subject;
        set
        {
            if (Set(ref subject, value))
                RaiseTexts();
        }
    }

    public string Email
    {
        get => email;
        set
        {
            if (Set(ref email, value))
                RaiseTexts();
        }
    }

    public string Phone
    {
        get => phone;
        set => Set(ref phone, value);
    }

    public string Room
    {
        get => room;
        set => Set(ref room, value);
    }

    public string Notes
    {
        get => notes;
        set => Set(ref notes, value);
    }

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(Name)
        ? string.IsNullOrWhiteSpace(Email) ? "Lehrkraft ohne Namen" : Email
        : Name;

    /// <summary>Zweite Zeile in der Liste: Fach, Kürzel und Adresse.</summary>
    [JsonIgnore]
    public string SubtitleText
    {
        get
        {
            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(ShortName))
                parts.Add(ShortName);

            if (!string.IsNullOrWhiteSpace(Subject))
                parts.Add(Subject);

            if (!string.IsNullOrWhiteSpace(Email))
                parts.Add(Email);

            return parts.Count == 0 ? "keine Angaben" : string.Join(" · ", parts);
        }
    }

    /// <summary>Die Adresse im Format für das Empfängerfeld.</summary>
    [JsonIgnore]
    public string MailAddress => string.IsNullOrWhiteSpace(Name)
        ? Email
        : $"{Name} <{Email}>";

    [JsonIgnore]
    public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

    public event PropertyChangedEventHandler? PropertyChanged;

    public override string ToString() =>
        string.IsNullOrWhiteSpace(Email) ? DisplayName : $"{DisplayName} · {Email}";

    private void RaiseTexts()
    {
        OnPropertyChanged(nameof(DisplayName));
        OnPropertyChanged(nameof(SubtitleText));
        OnPropertyChanged(nameof(MailAddress));
        OnPropertyChanged(nameof(HasEmail));
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
