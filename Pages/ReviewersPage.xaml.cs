using System.Collections.ObjectModel;
using mindvault.Controls;
using mindvault.Services;
using mindvault.Pages;
using mindvault.Utils;
using System.Diagnostics;

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

    public ReviewersPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);

        // Demo data matching the screenshot vibe
        Reviewers.Add(new ReviewerCard
        {
            Title = "Math Reviewer",
            Questions = 50,
            LearnedRatio = 0.26,   // 26%
            Due = 37
        });
    }

    // ===== Robust navigation wiring =====
    bool _wired;
    protected override void OnAppearing()
    {
        base.OnAppearing();
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
        // Get the tapped reviewer card
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            bool confirmed = await PageHelpers.SafeDisplayAlertAsync(this, "Delete Reviewer", 
                $"Are you sure you want to delete '{reviewer.Title}'?", 
                "Delete", "Cancel");
            
            if (confirmed)
            {
                Reviewers.Remove(reviewer);
                await PageHelpers.SafeDisplayAlertAsync(this, "Deleted", $"'{reviewer.Title}' has been removed.", "OK");
            }
        }
    }

    private async void OnViewCourseTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewersPage] OpenCourse() -> CourseReviewPage");
        await Navigator.PushAsync(new CourseReviewPage(), Navigation);
    }
}

public class ReviewerCard
{
    public string Title { get; set; } = string.Empty;
    public int Questions { get; set; }
    /// <summary>0..1</summary>
    public double LearnedRatio { get; set; }
    public int Due { get; set; }

    // Convenience texts for binding
    public string LearnedPercentText => $"{(int)(LearnedRatio * 100)}% Learned";
    public string DueText => $"{Due} due";
}
