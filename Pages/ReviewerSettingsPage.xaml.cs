using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class ReviewerSettingsPage : ContentPage
{
    public string ReviewerTitle { get; }

    public ReviewerSettingsPage(string reviewerTitle = "Math Reviewer")
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    private async void OnCloseTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewerSettingsPage] CloseSettingsToReviewers() -> ReviewersPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseSettingsToReviewers(),
            "Could not return to reviewers");
    }
}
