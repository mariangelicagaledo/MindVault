using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class ExportPage : ContentPage
{
    public string ReviewerTitle { get; }
    public int Questions { get; }

    public string QuestionsText => Questions.ToString();

    public ExportPage(string reviewerTitle = "Math Reviewer", int questions = 50)
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        Questions = questions;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    private async void OnExportTapped(object? sender, EventArgs e)
    {
        // Static/semi-functional: pretend export succeeded
        await PageHelpers.SafeDisplayAlertAsync(this, "Export", $"{ReviewerTitle} exported to TXT (demo).", "OK");
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ExportPage] Back() -> Previous page");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.Back(),
            "Could not go back");
    }

    private async void OnCloseTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ExportPage] Back() -> Previous page");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.Back(),
            "Could not go back");
    }
}
