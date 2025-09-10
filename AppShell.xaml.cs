namespace mindvault;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute(nameof(Pages.OnboardingPage), typeof(Pages.OnboardingPage));
        Routing.RegisterRoute(nameof(Pages.SetProfilePage), typeof(Pages.SetProfilePage));
        Routing.RegisterRoute(nameof(Pages.HomePage), typeof(Pages.HomePage));
        Routing.RegisterRoute(nameof(Pages.TitleReviewerPage), typeof(Pages.TitleReviewerPage));
        Routing.RegisterRoute(nameof(Pages.ReviewersPage), typeof(Pages.ReviewersPage));
        Routing.RegisterRoute(nameof(Pages.ReviewerEditorPage), typeof(Pages.ReviewerEditorPage));
        Routing.RegisterRoute(nameof(Pages.CourseReviewPage), typeof(Pages.CourseReviewPage));
        Routing.RegisterRoute(nameof(Pages.ReviewerSettingsPage), typeof(Pages.ReviewerSettingsPage));
        Routing.RegisterRoute(nameof(Pages.ImportPage), typeof(Pages.ImportPage));
        Routing.RegisterRoute(nameof(Pages.ExportPage), typeof(Pages.ExportPage));
        Routing.RegisterRoute(nameof(Pages.MultiplayerPage), typeof(Pages.MultiplayerPage));

    }
}
