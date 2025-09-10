using System.Collections.ObjectModel;
using mindvault.Controls;
using mindvault.Services;
using mindvault.Pages;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;

namespace mindvault.Pages;

public partial class ReviewersPage : ContentPage
{
    // Dropdown: only these two options
    public ObservableCollection<string> SortOptions { get; } =
        new() { "All", "Last Played" };

    private string _selectedSort = "Last Played";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (_selectedSort == value) return;
            _selectedSort = value ?? "All";
            OnPropertyChanged(nameof(SelectedSort));
            // For now this is a no-op; later you can reorder/filter based on _selectedSort.
        }
    }

    public ObservableCollection<ReviewerCard> Reviewers { get; } = new();

    readonly DatabaseService _db;

    public ReviewersPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        _db = ServiceHelper.GetRequiredService<DatabaseService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadFromDbAsync();
        WireOnce();
    }

    async Task LoadFromDbAsync()
    {
        Reviewers.Clear();
        var rows = await _db.GetReviewersAsync();
        foreach (var r in rows)
        {
            var cards = await _db.GetFlashcardsAsync(r.Id);
            Reviewers.Add(new ReviewerCard
            {
                Id = r.Id,
                Title = r.Title,
                Questions = cards.Count,
                LearnedRatio = (cards.Count == 0) ? 0 : (double)cards.Count(c => c.Learned) / cards.Count,
                Due = 0
            });
        }
    }

    // ===== Robust navigation wiring =====
    bool _wired;
    void WireOnce()
    {
        if (_wired) return;
        // Import pill
        ImportPill.GestureRecognizers.Add(new TapGestureRecognizer
        {
            Command = new Command(async () => await Navigator.PushAsync(new ImportPage(), Navigation))
        });
        _wired = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // If you add event handlers via +=, unsubscribe here to prevent leaks.
    }

    private async void OnDeleteTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            bool confirmed = await PageHelpers.SafeDisplayAlertAsync(this, "Delete Reviewer", 
                $"Are you sure you want to delete '{reviewer.Title}'?", 
                "Delete", "Cancel");
            if (confirmed)
            {
                await _db.DeleteReviewerCascadeAsync(reviewer.Id);
                Reviewers.Remove(reviewer);
                await PageHelpers.SafeDisplayAlertAsync(this, "Deleted", $"'{reviewer.Title}' has been removed.", "OK");
            }
        }
    }

    private async void OnViewCourseTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            Debug.WriteLine($"[ReviewersPage] OpenCourse() -> CourseReviewPage");
            await Navigator.PushAsync(new CourseReviewPage(reviewer.Title), Navigation);
        }
    }
}

public class ReviewerCard
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Questions { get; set; }
    /// <summary>0..1</summary>
    public double LearnedRatio { get; set; }
    public int Due { get; set; }

    // Convenience texts for binding
    public string LearnedPercentText => $"{(int)(LearnedRatio * 100)}% Learned";
    public string DueText => $"{Due} due";
}
