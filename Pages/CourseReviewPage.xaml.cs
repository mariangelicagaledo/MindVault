using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mindvault.Services;
using mindvault.Pages;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class CourseReviewPage : ContentPage, INotifyPropertyChanged
{
    public new string Title { get; }

    // simple deck
    public ObservableCollection<Card> Deck { get; } = new();

    int _index;
    bool _front = true;

    // stats (semi-functional demo)
    public int Avail => Deck.Count;
    int _seen;
    public int Seen { get => _seen; set { _seen = value; OnPropertyChanged(); } }
    int _learned;
    public int Learned { get => _learned; set { _learned = value; OnPropertyChanged(); } }
    int _skilled;
    public int Skilled { get => _skilled; set { _skilled = value; OnPropertyChanged(); } }
    int _memorized;
    public int Memorized { get => _memorized; set { _memorized = value; OnPropertyChanged(); } }

    public string FaceTag => _front ? "[Front]" : "[Back]";
    public string FaceText => Current is null ? "" : (_front ? Current.Question : Current.Answer);

    // progress "width" proxy (for the simple Box width). Adjusted in OnSizeAllocated.
    double _progressWidth;
    public double ProgressWidth { get => _progressWidth; set { _progressWidth = value; OnPropertyChanged(); } }

    Card? Current => (_index >= 0 && _index < Deck.Count) ? Deck[_index] : null;

    public CourseReviewPage(string title = "Math Reviewer")
    {
        InitializeComponent();
        Title = title;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);

        // demo data - you can customize this based on the title
        if (title.Contains("Math", StringComparison.OrdinalIgnoreCase))
        {
            Deck.Add(new Card("What is 8 + 5?", "13"));
            Deck.Add(new Card("What is 4 + 5?", "9"));
            Deck.Add(new Card("What is 7 + 6?", "13"));
        }
        else
        {
            Deck.Add(new Card("Sample Question 1?", "Sample Answer 1"));
            Deck.Add(new Card("Sample Question 2?", "Sample Answer 2"));
            Deck.Add(new Card("Sample Question 3?", "Sample Answer 3"));
        }

        UpdateBindingsAll();
    }

    // ---- actions
    private void OnFlip(object? s, TappedEventArgs e)
    {
        _front = !_front;
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
    }

    private void OnSkip(object? s, TappedEventArgs e) => Next();

    private void OnFail(object? s, TappedEventArgs e)
    {
        Seen++;
        Next();
    }

    private void OnPass(object? s, TappedEventArgs e)
    {
        Seen++;
        Memorized++;        // simple demo behavior
        Next();
    }

    private async void OnCloseTapped(object? s, EventArgs e)
    {
        Debug.WriteLine($"[CourseReviewPage] CloseCourseToReviewers() -> ReviewersPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseCourseToReviewers(),
            "Could not return to reviewers");
    }

    private async void OnSettingsTapped(object? s, EventArgs e)
    {
        Debug.WriteLine($"[CourseReviewPage] OpenReviewerSettings() -> ReviewerSettingsPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.OpenReviewerSettings(),
            "Could not open settings");
    }

    // ---- helpers
    void Next()
    {
        _index = (_index + 1) % (Deck.Count == 0 ? 1 : Deck.Count);
        _front = true;
        UpdateBindingsAll();
    }

    void UpdateBindingsAll()
    {
        OnPropertyChanged(nameof(Avail));
        OnPropertyChanged(nameof(Seen));
        OnPropertyChanged(nameof(Learned));
        OnPropertyChanged(nameof(Skilled));
        OnPropertyChanged(nameof(Memorized));
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
        UpdateProgressWidth();
    }

    void UpdateProgressWidth()
    {
        // crude progress: seen/avail
        double ratio = (Avail == 0) ? 0 : Math.Clamp((double)Seen / Avail, 0, 1);
        // convert ratio to width using the current page width minus horizontal padding
        ProgressWidth = (Width - 32) * ratio; // 16 left + 16 right page padding
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);
        UpdateProgressWidth();
    }

    // model
    public record Card(string Question, string Answer);

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
