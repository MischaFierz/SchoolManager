using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Text;

namespace SchoolManager.App.Data;

/// <summary>
/// Hält die Lehrkräfte. Wird von der Lehrkräfte-Seite und von der
/// Empfängerauswahl der E-Mail-Seite gemeinsam benutzt und lässt sich als
/// CSV-Datei aus- und einlesen.
/// </summary>
public sealed class TeacherStore
{
    private const string FileName = "lehrkraefte.json";

    private static readonly string[] Header =
        ["Name", "Kürzel", "Fach", "E-Mail", "Telefon", "Raum", "Notiz"];

    public TeacherStore()
    {
        Items = new ObservableCollection<Teacher>(
            LocalStore.Load<Teacher>(FileName).OrderBy(item => item.DisplayName));

        foreach (var item in Items)
            item.PropertyChanged += Item_PropertyChanged;

        Items.CollectionChanged += Items_CollectionChanged;
    }

    public ObservableCollection<Teacher> Items { get; }

    public event Action? Changed;

    public event Action<string>? SaveFailed;

    public Teacher Add(string name = "")
    {
        var teacher = new Teacher { Name = name };
        Items.Add(teacher);
        return teacher;
    }

    public void Remove(Teacher teacher) => Items.Remove(teacher);

    public void Save()
    {
        if (LocalStore.TrySave(FileName, Items) is { } error)
            SaveFailed?.Invoke(error);
    }

    /// <summary>Schreibt die Liste als CSV mit Semikolon - so öffnet Excel sie direkt.</summary>
    public void ExportCsv(string path)
    {
        var builder = new StringBuilder();
        builder.AppendLine(string.Join(';', Header));

        foreach (var teacher in Items)
            builder.AppendLine(string.Join(';', new[]
            {
                teacher.Name, teacher.ShortName, teacher.Subject,
                teacher.Email, teacher.Phone, teacher.Room, teacher.Notes
            }.Select(Quote)));

        // Mit BOM, damit Excel die Umlaute richtig liest.
        File.WriteAllText(path, builder.ToString(), new UTF8Encoding(true));
    }

    /// <summary>
    /// Liest eine CSV-Datei ein und gibt zurück, wie viele Lehrkräfte
    /// dazugekommen sind. Bekannte Adressen werden aktualisiert, nicht doppelt
    /// angelegt.
    /// </summary>
    public (int Added, int Updated) ImportCsv(string path)
    {
        var lines = File.ReadAllLines(path, Encoding.UTF8);

        if (lines.Length == 0)
            throw new InvalidOperationException("Die Datei ist leer.");

        var added = 0;
        var updated = 0;
        var start = lines[0].Contains("Name", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

        for (var i = start; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i]))
                continue;

            var fields = SplitCsvLine(lines[i]);

            if (fields.Count == 0 || fields.All(string.IsNullOrWhiteSpace))
                continue;

            var email = Field(fields, 3);

            var existing = Items.FirstOrDefault(t =>
                !string.IsNullOrWhiteSpace(email) &&
                string.Equals(t.Email, email, StringComparison.OrdinalIgnoreCase));

            var teacher = existing ?? new Teacher();

            teacher.Name = Field(fields, 0);
            teacher.ShortName = Field(fields, 1);
            teacher.Subject = Field(fields, 2);
            teacher.Email = email;
            teacher.Phone = Field(fields, 4);
            teacher.Room = Field(fields, 5);
            teacher.Notes = Field(fields, 6);

            if (existing is null)
            {
                Items.Add(teacher);
                added++;
            }
            else
            {
                updated++;
            }
        }

        if (added == 0 && updated == 0)
            throw new InvalidOperationException("In der Datei stand keine verwertbare Zeile.");

        Save();
        return (added, updated);
    }

    private static string Field(List<string> fields, int index) =>
        index < fields.Count ? fields[index].Trim() : "";

    private static string Quote(string value) =>
        value.Contains(';') || value.Contains('"') || value.Contains('\n')
            ? $"\"{value.Replace("\"", "\"\"")}\""
            : value;

    /// <summary>Zerlegt eine CSV-Zeile und beachtet Anführungszeichen.</summary>
    private static List<string> SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];

            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;

                case ';' or ',':
                    fields.Add(current.ToString());
                    current.Clear();
                    break;

                default:
                    current.Append(c);
                    break;
            }
        }

        fields.Add(current.ToString());
        return fields;
    }

    private void Items_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        foreach (var item in e.OldItems?.OfType<Teacher>() ?? [])
            item.PropertyChanged -= Item_PropertyChanged;

        foreach (var item in e.NewItems?.OfType<Teacher>() ?? [])
            item.PropertyChanged += Item_PropertyChanged;

        Changed?.Invoke();
        Save();
    }

    private void Item_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        Changed?.Invoke();
        Save();
    }
}
