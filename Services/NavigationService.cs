using System.Diagnostics;

namespace mindvault.Services;

public static class NavigationService
{
    public static Task Go(string route) => Shell.Current.GoToAsync(route);
    public static Task Back() => Shell.Current.GoToAsync("..");
    public static Task ToRoot() => Shell.Current.GoToAsync($"//{nameof(Pages.ReviewersPage)}");

    // Burger:
    public static Task OpenImport() => Go(nameof(Pages.ImportPage));
    public static Task OpenExport() => Go(nameof(Pages.ExportPage));
    public static Task OpenTitle() => Go(nameof(Pages.TitleReviewerPage));

    // TitleReviewerPage:
    public static Task CreateNewReviewer(int id, string title)
        => Go($"///{nameof(Pages.ReviewerEditorPage)}?id={id}&title={System.Uri.EscapeDataString(title)}");
    public static Task CloseTitleToReviewers() => ToRoot();

    // ReviewerEditorPage:
    public static Task CloseEditorToTitle() => Back(); // returns to TitleReviewerPage
    public static Task CloseEditorToReviewers() => ToRoot();

    // ReviewersPage:
    public static Task OpenCourse() => Go(nameof(Pages.CourseReviewPage));
    public static Task OpenReviewerSettings() => Go(nameof(Pages.ReviewerSettingsPage));

    // CourseReviewPage:
    public static Task CloseCourseToReviewers() => Back();

    // ReviewerSettingsPage:
    public static async Task CloseSettingsToReviewers()
    {
        try
        {
            await Back();
        }
        catch
        {
            // Fallback if relative back fails
            await Go(nameof(Pages.CourseReviewPage));
        }
    }
}
