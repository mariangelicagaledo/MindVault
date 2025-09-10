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
    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();

    public new string Title { get; }
    public int ReviewerId { get; private set; }

    // simple deck
    public ObservableCollection<Card> Deck { get; } = new();

    int _index;
    bool _front = true;
    bool _loaded;

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

    // Preferred ctor: pass id and title
    public CourseReviewPage(int reviewerId, string title)
    {
        InitializeComponent();
        ReviewerId = reviewerId;
        Title = title;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    // Back-compat ctor: title only (resolve id on appearing)
    public CourseReviewPage(string title = "Math Reviewer")
    {
        InitializeComponent();
        Title = title;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (ReviewerId <= 0)
        {
            // Resolve by title if only title was provided
            var reviewers = await _db.GetReviewersAsync();
            var match = reviewers.FirstOrDefault(r => r.Title == Title);
            if (match is not null)
                ReviewerId = match.Id;
        }

        if (_loaded) return;
        await LoadDeckAsync();
        _loaded = true;
    }

    async Task LoadDeckAsync()
    {
        Deck.Clear();
        if (ReviewerId > 0)
        {
            var cards = await _db.GetFlashcardsAsync(ReviewerId);
            foreach (var c in cards)
                Deck.Add(new Card(c.Question, c.Answer));
        }
        _index = 0;
        _front = true;
        Seen = 0; Learned = 0; Skilled = 0; Memorized = 0;
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
