using System.Diagnostics;
using Microsoft.Maui.ApplicationModel;

namespace mindvault.Services;

public static class NavigationService
{
    static Task GoOnMainAsync(string route)
        => MainThread.IsMainThread
            ? Shell.Current.GoToAsync(route)
            : MainThread.InvokeOnMainThreadAsync(async () => await Shell.Current.GoToAsync(route));

    public static Task Go(string route) => GoOnMainAsync(route);
    public static Task Back() => GoOnMainAsync("..");
    public static Task ToRoot() => GoOnMainAsync($"//{nameof(Pages.ReviewersPage)}");

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

    // Reviewer Settings (use absolute route to avoid relative routing error)
    public static Task OpenReviewerSettings() => Go("///ReviewerSettingsPage");
    public static Task OpenReviewerSettings(string reviewerTitle)
        => Go($"///ReviewerSettingsPage?reviewerTitle={System.Uri.EscapeDataString(reviewerTitle)}");
    public static Task OpenReviewerSettings(int reviewerId, string reviewerTitle)
        => Go($"///ReviewerSettingsPage?reviewerId={reviewerId}&reviewerTitle={System.Uri.EscapeDataString(reviewerTitle)}");

    // CourseReviewPage:
    public static Task CloseCourseToReviewers() => Go("///ReviewersPage");

    // ReviewerSettingsPage:
    public static async Task CloseSettingsToReviewers()
    {
        try
        {
            await Back(); // return to the actual instance of CourseReviewPage on the stack
        }
        catch
        {
            await Go("///ReviewersPage"); // if no stack, go to list rather than a generic CourseReviewPage
        }
    }
}
