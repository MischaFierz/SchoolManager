using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Eine Aufgabe innerhalb eines Auftrags; enthält Leistungsdetails.</summary>
public sealed class WorkTask : WorkNode
{
    public WorkTask()
    {
        Entries.CollectionChanged += (_, e) =>
        {
            foreach (var entry in e.NewItems?.OfType<WorkEntry>() ?? [])
                entry.Parent = this;

            RaiseTotals();
            RaiseDoneChanged();
            RaiseChanged();
        };
    }

    // Populate: sonst laesst System.Text.Json die Sammlung beim Laden leer,
    // weil die Eigenschaft absichtlich keinen Setter hat.
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<WorkEntry> Entries { get; } = [];

    [JsonIgnore]
    public override int TotalMinutes => Entries.Sum(entry => entry.TotalMinutes);

    [JsonIgnore]
    public override string DoneSummary
    {
        get
        {
            if (Entries.Count == 0)
                return IsDone ? "erledigt" : "keine Leistungsdetails";

            var done = Entries.Count(entry => entry.IsDone);

            return IsDone
                ? $"erledigt · {done} von {Entries.Count} Leistungsdetails"
                : $"{done} von {Entries.Count} Leistungsdetails erledigt";
        }
    }

    /// <summary>Name des Auftrags, zu dem die Aufgabe gehört - für die Gesamtliste.</summary>
    [JsonIgnore]
    public string OrderTitle => Parent?.DisplayTitle ?? "Ohne Auftrag";

    [JsonIgnore]
    public override string LevelName => "Aufgabe";

    protected override string DefaultTitle => "Aufgabe ohne Titel";
}
