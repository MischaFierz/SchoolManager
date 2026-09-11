using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace SchoolManager.App.Data;

/// <summary>Ein Auftrag - oberste Ebene, enthält Aufgaben.</summary>
public sealed class WorkOrder : WorkNode
{
    public WorkOrder()
    {
        Tasks.CollectionChanged += (_, e) =>
        {
            foreach (var task in e.NewItems?.OfType<WorkTask>() ?? [])
                task.Parent = this;

            RaiseTotals();
            RaiseDoneChanged();
            RaiseChanged();
        };
    }

    // Populate: sonst laesst System.Text.Json die Sammlung beim Laden leer,
    // weil die Eigenschaft absichtlich keinen Setter hat.
    [JsonObjectCreationHandling(JsonObjectCreationHandling.Populate)]
    public ObservableCollection<WorkTask> Tasks { get; } = [];

    [JsonIgnore]
    public override int TotalMinutes => Tasks.Sum(t => t.TotalMinutes);

    [JsonIgnore]
    public override string DoneSummary
    {
        get
        {
            if (Tasks.Count == 0)
                return IsDone ? "erledigt" : "keine Aufgaben";

            var done = Tasks.Count(t => t.IsDone);

            return IsDone
                ? $"erledigt · {done} von {Tasks.Count} Aufgaben"
                : $"{done} von {Tasks.Count} Aufgaben erledigt";
        }
    }

    [JsonIgnore]
    public override string LevelName => "Auftrag";

    protected override string DefaultTitle => "Auftrag ohne Titel";
}
