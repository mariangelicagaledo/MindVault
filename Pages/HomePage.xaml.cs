using mindvault.Controls;
using mindvault.Pages;
using mindvault.Utils;

namespace mindvault.Pages;

public partial class HomePage : ContentPage
{
    public HomePage()
    {
        InitializeComponent();
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
    }

    private async void OnCreateReviewerTapped(object sender, EventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await Shell.Current.GoToAsync("///TitleReviewerPage"), 
            "Could not navigate to Create Reviewer");
    }

    private async void OnBrowseReviewerTapped(object sender, EventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await Shell.Current.GoToAsync("///ReviewersPage"), 
            "Could not navigate to Browse Reviewers");
    }
}
