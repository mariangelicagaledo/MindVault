using mindvault.Services;

namespace mindvault.Pages;

public partial class TaglinePage : ContentPage
{
    bool _navigated;

    public TaglinePage()
    {
        InitializeComponent();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // prevent double navigation if page re-appears
        if (_navigated) return;
        _navigated = true;

        // Defer navigation off the appearing call to avoid Android crashes
        Dispatcher.Dispatch(async () =>
        {
            try
            {
                if (OnboardingState.IsCompleted)
                {
                    var route = ProfileState.HasName ? "///HomePage" : "///SetProfilePage";
                    await Navigator.GoToAsync(route);
                    return;
                }

                await Task.Delay(2000); // 2 seconds splash
                await Navigator.GoToAsync("///OnboardingPage");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[TaglinePage] Navigation error: {ex}");
            }
        });
    }
}
