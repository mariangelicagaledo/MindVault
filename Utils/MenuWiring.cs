using mindvault.Controls;
using mindvault.Pages;
using mindvault.Services;

namespace mindvault.Utils;

public static class MenuWiring
{
    public static void Wire(BottomSheetMenu menu, INavigation nav)
    {
        // Create Reviewer
        menu.CreateTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(TitleReviewerPage));
            else
                await Navigator.PushAsync(new TitleReviewerPage(), nav);
        };

        // Browse Reviewer
        menu.BrowseTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(ReviewersPage));
            else
                await Navigator.PushAsync(new ReviewersPage(), nav);
        };

        // Multiplayer Mode
        menu.MultiplayerTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(MultiplayerPage));
            else
                await Navigator.PushAsync(new MultiplayerPage(), nav);
        };

        // Import
        menu.ImportTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(ImportPage));
            else
                await Navigator.PushAsync(new ImportPage(), nav);
        };

        // Export
        menu.ExportTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(ExportPage));
            else
                await Navigator.PushAsync(new ExportPage(), nav);
        };
    }
}
