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
    public static Task CreateNewReviewer() => Go(nameof(Pages.ReviewerEditorPage));
    public static Task CloseTitleToReviewers() => ToRoot();

    // ReviewerEditorPage:
    public static Task CloseEditorToTitle() => Back(); // returns to TitleReviewerPage

    // ReviewersPage:
    public static Task OpenCourse() => Go(nameof(Pages.CourseReviewPage));
    public static Task OpenReviewerSettings() => Go(nameof(Pages.ReviewerSettingsPage));

    // CourseReviewPage:
    public static Task CloseCourseToReviewers() => Back();

    // ReviewerSettingsPage:
    public static Task CloseSettingsToReviewers() => Back();
}
