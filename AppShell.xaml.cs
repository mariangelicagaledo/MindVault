namespace mindvault;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register only pages not declared as ShellContent in AppShell.xaml
        // MultiplayerPage is navigated via Shell route but not present in XAML
        Routing.RegisterRoute(nameof(Pages.MultiplayerPage), typeof(Pages.MultiplayerPage));
    }
}
