using System.Globalization;
using System.Windows;
using SchoolManager.App.Data;

namespace SchoolManager.App.Dialogs;

/// <summary>
/// Fenster zum Anlegen und Bearbeiten einer Lektion samt Lehrkraft. Gearbeitet
/// wird auf einer Kopie; „Abbrechen“ lässt den Stundenplan unverändert.
/// </summary>
public partial class LessonDialog : Window
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    private readonly TimetableEntry draft;
    private readonly TeacherStore teachers;

    public LessonDialog(TimetableEntry entry, TeacherStore teachers, string heading, string hint)
    {
        draft = entry.Clone();
        this.teachers = teachers;

        InitializeComponent();

        HeadingText.Text = heading;
        HintText.Text = hint;

        DayBox.ItemsSource = DayChoice.All;
        TeacherBox.ItemsSource = BuildTeacherChoices();

        ShowDraft();

        SourceInitialized += (_, _) => DarkTitleBar.Apply(this);
        Loaded += (_, _) =>
        {
            SubjectBox.Focus();
            SubjectBox.SelectAll();
        };
    }

    /// <summary>Die bearbeitete Kopie - nach „Speichern“ zu übernehmen.</summary>
    public TimetableEntry Result => draft;

    /// <summary>
    /// Fach und Lehrkraft, wie sie zugeordnet werden sollen. Leer heisst: die
    /// Zuordnung für dieses Fach löschen.
    /// </summary>
    public (string Subject, string? TeacherId) TeacherAssignment =>
        (draft.Subject, draft.TeacherId);

    private List<TeacherChoice> BuildTeacherChoices()
    {
        var choices = new List<TeacherChoice> { new(null) };
        choices.AddRange(teachers.Items.Select(teacher => new TeacherChoice(teacher)));
        return choices;
    }

    private void ShowDraft()
    {
        SubjectBox.Text = draft.Subject;
        RoomBox.Text = draft.Room;
        NoteBox.Text = draft.Note;
        StartBox.Text = draft.StartTimeText;
        DurationBox.Text = draft.DurationText;
        DateBox.Text = draft.DateText;
        WeeklyBox.IsChecked = draft.IsWeekly;

        DayBox.SelectedItem = DayChoice.All.FirstOrDefault(choice => choice.Value == draft.Day)
                              ?? DayChoice.All[0];

        var choices = (List<TeacherChoice>)TeacherBox.ItemsSource;

        TeacherBox.SelectedItem = choices.FirstOrDefault(choice => choice.Teacher?.Id == draft.TeacherId)
                                  ?? choices[0];

        ShowWeekly();
    }

    private void Weekly_Changed(object sender, RoutedEventArgs e) => ShowWeekly();

    private void ShowWeekly()
    {
        var weekly = WeeklyBox.IsChecked == true;

        DayPanel.Visibility = weekly ? Visibility.Visible : Visibility.Collapsed;
        DatePanel.Visibility = weekly ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var subject = SubjectBox.Text.Trim();

        if (subject.Length == 0)
        {
            Fail("Bitte ein Fach eintragen.", SubjectBox);
            return;
        }

        if (!TimeSpan.TryParse(StartBox.Text.Trim(), Culture, out var start) ||
            start >= TimeSpan.FromDays(1))
        {
            Fail("Der Beginn muss als hh:mm angegeben werden, etwa 08:15.", StartBox);
            return;
        }

        if (!TimeText.TryParse(DurationBox.Text.Trim(), out var duration) || duration <= 0)
        {
            Fail("Die Dauer muss etwa 45, 1:30 oder 1,5h sein.", DurationBox);
            return;
        }

        var weekly = WeeklyBox.IsChecked == true;
        var date = draft.Date;

        if (!weekly)
        {
            if (!DateTime.TryParse(DateBox.Text.Trim(), Culture, DateTimeStyles.None, out var parsed))
            {
                Fail("Das Datum muss als tt.mm.jjjj angegeben werden.", DateBox);
                return;
            }

            date = new DateTimeOffset(parsed.Date);
        }

        draft.Subject = subject;
        draft.Room = RoomBox.Text.Trim();
        draft.Note = NoteBox.Text;
        draft.StartMinutes = (int)start.TotalMinutes;
        draft.DurationMinutes = duration;
        draft.IsWeekly = weekly;
        draft.Date = date;
        draft.Day = (DayBox.SelectedItem as DayChoice)?.Value ?? DayOfWeek.Monday;
        draft.TeacherId = (TeacherBox.SelectedItem as TeacherChoice)?.Teacher?.Id;

        DialogResult = true;
    }

    private void Fail(string message, System.Windows.Controls.Control focus)
    {
        ErrorText.Text = message;
        focus.Focus();
    }

    /// <summary>Ein Eintrag der Wochentag-Auswahl.</summary>
    public sealed class DayChoice(DayOfWeek value)
    {
        public DayOfWeek Value { get; } = value;

        public override string ToString() => Culture.DateTimeFormat.GetDayName(Value);

        public static DayChoice[] All { get; } =
        [
            new(DayOfWeek.Monday), new(DayOfWeek.Tuesday), new(DayOfWeek.Wednesday),
            new(DayOfWeek.Thursday), new(DayOfWeek.Friday), new(DayOfWeek.Saturday),
            new(DayOfWeek.Sunday)
        ];
    }

    /// <summary>Ein Eintrag der Lehrkraft-Auswahl.</summary>
    public sealed class TeacherChoice(Teacher? teacher)
    {
        public Teacher? Teacher { get; } = teacher;

        public override string ToString() =>
            Teacher is null ? "— keine Lehrkraft —" : Teacher.ToString();
    }
}
