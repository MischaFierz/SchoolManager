using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SchoolManager.App.Controls;

/// <summary>
/// Ein Datumsfeld: tippen wie bisher, oder rechts auf das Symbol klicken und
/// den Tag aus einem kleinen Monatsblatt auswählen.
///
/// Nach aussen verhält sich das Feld wie ein Textfeld - es trägt den Text im
/// Format tt.mm.jjjj, genau wie die Eigenschaften der Einträge ihn erwarten.
/// Deshalb musste an den Daten selbst nichts geändert werden, und ein leeres
/// Feld heisst weiterhin "kein Datum".
///
/// Das Monatsblatt ist bewusst selbst gebaut und nicht der eingebaute Kalender
/// von WPF: Der bringt sein eigenes helles Aussehen mit, das sich in dieser
/// dunklen Oberfläche nur mit viel Mühe zurechtbiegen liesse.
/// </summary>
public partial class DateBox : UserControl
{
    private static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("de-CH");

    /// <summary>Das Datum als Text, tt.mm.jjjj; leer heisst: keines.</summary>
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(nameof(Text), typeof(string), typeof(DateBox),
            new FrameworkPropertyMetadata("",
                FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnTextChanged));

    /// <summary>Der gerade angezeigte Monat im Blatt.</summary>
    private DateTime shownMonth = DateTime.Today;

    /// <summary>
    /// Wann das Blatt zuletzt zuging. Ein Klick auf das Symbol schliesst ein
    /// offenes Blatt bereits von sich aus; ohne diese Merkhilfe ginge es
    /// unmittelbar darauf wieder auf und liesse sich nie schliessen.
    /// </summary>
    private DateTime closedAt = DateTime.MinValue;

    public DateBox()
    {
        InitializeComponent();
        ShowDayNames();

        // Die Schriftgrösse des Feldes kommt sonst aus dem Aussehen der
        // Anwendung. Nur wo eine ausdrücklich gesetzt ist - die To-Do-Zeilen
        // schreiben kleiner - wird sie übernommen.
        Loaded += (_, _) =>
        {
            if (ReadLocalValue(FontSizeProperty) != DependencyProperty.UnsetValue)
                Field.FontSize = FontSize;
        };
    }

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>
    /// Reicht den Tastaturfokus ins Textfeld weiter. Wer hierher springt - der
    /// Tabulator oder ein Dialog nach einer falschen Eingabe -, landet sonst
    /// auf der Hülle und kann nichts tippen.
    /// </summary>
    protected override void OnGotFocus(RoutedEventArgs e)
    {
        base.OnGotFocus(e);

        if (!Field.IsKeyboardFocusWithin)
            Field.Focus();
    }

    /// <summary>Das Datum hinter dem Text, oder null bei leer oder unlesbar.</summary>
    public DateTime? SelectedDate => Parse(Text);

    private static void OnTextChanged(DependencyObject sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is DateBox box)
            box.ShowValidity();
    }

    // ==== Eingabe von Hand ====

    /// <summary>
    /// Färbt den Rahmen rot, solange der Text kein Datum ist. Die Einträge
    /// setzen unlesbare Eingaben ohnehin zurück - der Rahmen sagt, warum.
    /// </summary>
    private void ShowValidity()
    {
        var ok = string.IsNullOrWhiteSpace(Text) || Parse(Text) is not null;

        Field.BorderBrush = ok
            ? (Brush)FindResource("Line")
            : (Brush)FindResource("Error");
    }

    private void Field_LostFocus(object sender, RoutedEventArgs e) => ShowValidity();

    // ==== Monatsblatt ====

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        // Der Klick, der ein offenes Blatt geschlossen hat, darf es nicht
        // gleich wieder öffnen.
        if (DateTime.Now - closedAt < TimeSpan.FromMilliseconds(250))
            return;

        shownMonth = SelectedDate ?? DateTime.Today;

        ShowMonth();
        MonthPopup.IsOpen = true;
    }

    private void MonthPopup_Closed(object? sender, EventArgs e) => closedAt = DateTime.Now;

    private void PreviousMonth_Click(object sender, RoutedEventArgs e)
    {
        shownMonth = shownMonth.AddMonths(-1);
        ShowMonth();
    }

    private void NextMonth_Click(object sender, RoutedEventArgs e)
    {
        shownMonth = shownMonth.AddMonths(1);
        ShowMonth();
    }

    private void Today_Click(object sender, RoutedEventArgs e) => Choose(DateTime.Today);

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        Text = "";
        MonthPopup.IsOpen = false;
        Field.Focus();
    }

    /// <summary>Übernimmt den Tag und schliesst das Blatt.</summary>
    private void Choose(DateTime day)
    {
        Text = day.ToString("dd.MM.yyyy", Culture);
        MonthPopup.IsOpen = false;
        Field.Focus();
    }

    /// <summary>Die Kopfzeile Mo bis So; sie ändert sich nie.</summary>
    private void ShowDayNames()
    {
        // Der erste Montag eines beliebigen Jahres - von da an sieben Tage.
        var monday = new DateTime(2024, 1, 1);

        for (var i = 0; i < 7; i++)
        {
            DayNames.Children.Add(new TextBlock
            {
                Text = monday.AddDays(i).ToString("ddd", Culture)[..2],
                FontFamily = (FontFamily)FindResource("UiFont"),
                FontSize = 11,
                Foreground = (Brush)FindResource("TextFaint"),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 2)
            });
        }
    }

    /// <summary>
    /// Baut das Blatt des angezeigten Monats auf: immer sechs volle Wochen ab
    /// dem Montag vor dem Monatsersten, damit nichts springt. Die Tage der
    /// Nachbarmonate sind blasser, lassen sich aber genauso wählen.
    /// </summary>
    private void ShowMonth()
    {
        MonthLabel.Text = shownMonth.ToString("MMMM yyyy", Culture);

        DayGrid.Children.Clear();

        var first = new DateTime(shownMonth.Year, shownMonth.Month, 1);
        var offset = ((int)first.DayOfWeek + 6) % 7;
        var start = first.AddDays(-offset);
        var selected = SelectedDate;

        for (var i = 0; i < 42; i++)
        {
            var day = start.AddDays(i);
            var button = new Button
            {
                Content = day.Day.ToString(Culture),
                Style = (Style)FindResource("DayCell"),
                Tag = day,
                ToolTip = day.ToString("dddd, dd.MM.yyyy", Culture)
            };

            if (day.Month != shownMonth.Month)
                button.Foreground = (Brush)FindResource("TextFaint");

            if (day == DateTime.Today)
                button.FontWeight = FontWeights.Bold;

            // Der gewählte Tag ist hinterlegt - so sieht man beim Öffnen sofort,
            // worauf das Feld gerade steht.
            if (selected is { } value && day == value.Date)
            {
                button.Background = (Brush)FindResource("Accent");
                button.Foreground = (Brush)FindResource("AccentText");
            }

            button.Click += (_, _) => Choose(day);

            DayGrid.Children.Add(button);
        }
    }

    /// <summary>Liest ein Datum, wie es auch die Einträge selbst lesen.</summary>
    private static DateTime? Parse(string? text) =>
        !string.IsNullOrWhiteSpace(text)
        && DateTime.TryParse(text, Culture, DateTimeStyles.None, out var parsed)
            ? parsed.Date
            : null;
}
