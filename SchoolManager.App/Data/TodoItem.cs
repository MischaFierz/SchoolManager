using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SchoolManager.App.Data;

/// <summary>Eine Aufgabe auf der To-Do-Liste.</summary>
public sealed class TodoItem : INotifyPropertyChanged
{
    private string text = "";
    private bool isDone;

    public string Text
    {
        get => text;
        set => Set(ref text, value);
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
        }
    }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? DoneAt { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;

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
