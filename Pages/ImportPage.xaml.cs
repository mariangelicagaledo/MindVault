using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class ImportPage : ContentPage
{
    public string ReviewerTitle { get; }
    public int Questions { get; }

    public string QuestionsText => Questions.ToString();

    public ImportPage(string reviewerTitle = "Science Reviewer", int questions = 75)
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        Questions = questions;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
    }

    private async void OnImportTapped(object? sender, EventArgs e)
    {
        // Semi-functional: pretend it worked
        await PageHelpers.SafeDisplayAlertAsync(this, "Import", $"{ReviewerTitle} imported (demo).", "OK");
    }

    private async void OnCloseTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ImportPage] Back() -> Previous page");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.Back(),
            "Could not go back");
    }
}
