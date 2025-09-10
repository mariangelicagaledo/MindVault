using mindvault.Services;

namespace mindvault.Pages;

public partial class TaglinePage : ContentPage
{
    bool _navigated;

    public TaglinePage()
    {
        InitializeComponent();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // prevent double navigation if page re-appears
        if (_navigated) return;

        _navigated = true;

        // If onboarding completed: choose destination based on whether profile name exists
        if (OnboardingState.IsCompleted)
        {
            var route = ProfileState.HasName ? "///HomePage" : "///SetProfilePage";
            await Shell.Current.GoToAsync(route);
            return;
        }

        await Task.Delay(2000); // 2 seconds

        // IMPORTANT: use an ABSOLUTE Shell route for ShellContent
        // This fixes: "Relative routing to shell elements is not supported"
        await Shell.Current.GoToAsync("///OnboardingPage");
    }
}
