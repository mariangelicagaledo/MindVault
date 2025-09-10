using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class TitleReviewerPage : ContentPage
{
    public TitleReviewerPage()
    {
        InitializeComponent();
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
    }

    private async void OnCreateNewTapped(object sender, EventArgs e)
    {
        var title = TitleEntry?.Text?.Trim();
        if (string.IsNullOrEmpty(title))
        {
                    await PageHelpers.SafeDisplayAlertAsync(this, "Oops", "Please enter a title for your reviewer.", "OK");
        return;
    }

    Debug.WriteLine($"[TitleReviewerPage] CreateNewReviewer() -> ReviewerEditorPage");
    await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CreateNewReviewer(),
        "Could not create new reviewer");
    }
} 