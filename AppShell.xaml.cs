using System;
using Microsoft.Maui.ApplicationModel;

namespace mindvault;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register only pages not declared as ShellContent in AppShell.xaml
        // MultiplayerPage is navigated via Shell route but not present in XAML
        Routing.RegisterRoute(nameof(Pages.MultiplayerPage), typeof(Pages.MultiplayerPage));
        Routing.RegisterRoute(nameof(Pages.ProfileSettingsPage), typeof(Pages.ProfileSettingsPage));
    }

    protected override bool OnBackButtonPressed()
    {
        // If we can go back in Shell stack, do it
        var nav = Shell.Current?.Navigation;
        if (nav is not null && nav.NavigationStack.Count > 1)
        {
            _ = Services.Navigator.GoToAsync("..");
            return true; // handled
        }

        // No back stack. If we are on Home, minimize the app (Android); otherwise go Home
        var route = Shell.Current?.CurrentState?.Location.ToString() ?? string.Empty;
        var onHome = route.EndsWith("HomePage", StringComparison.OrdinalIgnoreCase);
        if (onHome)
        {
#if ANDROID
            try { Microsoft.Maui.ApplicationModel.Platform.CurrentActivity?.MoveTaskToBack(true); } catch { }
            return true;
#else
            return base.OnBackButtonPressed();
#endif
        }

        _ = Services.Navigator.GoToAsync("///HomePage");
        return true;
    }
}
