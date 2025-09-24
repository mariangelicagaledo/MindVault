using mindvault.Controls;
using mindvault.Pages;
using mindvault.Services;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Devices;
using System.IO;
using System.Linq;
using System.Collections.Generic;

namespace mindvault.Utils;

public static class MenuWiring
{
    public static void Wire(BottomSheetMenu menu, INavigation nav)
    {
        // Header tap -> Home (absolute to root)
        menu.HeaderTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync($"///{nameof(HomePage)}");
            else
                await Navigator.PopToRootAsync(nav);
        };

        // Create Reviewer (absolute navigation to reset stack)
        menu.CreateTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync($"///{nameof(TitleReviewerPage)}");
            else
                await Navigator.PushAsync(new TitleReviewerPage(), nav);
        };

        // Browse Reviewer (absolute navigation to reset stack)
        menu.BrowseTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync($"///{nameof(ReviewersPage)}");
            else
                await Navigator.PushAsync(new ReviewersPage(), nav);
        };

        // Multiplayer Mode (registered route, keep normal push)
        menu.MultiplayerTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(MultiplayerPage));
            else
                await Navigator.PushAsync(new MultiplayerPage(), nav);
        };

        // Import -> perform same flow as ReviewersPage import button
        menu.ImportTapped += async (_, __) =>
        {
            try
            {
                var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.Android, new[] { "text/plain" } },
                    { DevicePlatform.iOS, new[] { "public.plain-text" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } },
                    { DevicePlatform.WinUI, new[] { ".txt" } },
                });

                var pick = await FilePicker.PickAsync(new PickOptions
                {
                    PickerTitle = "Select export file",
                    FileTypes = fileTypes
                });
                if (pick is null) return;

                string content;
                using (var stream = await pick.OpenReadAsync())
                using (var reader = new StreamReader(stream))
                    content = await reader.ReadToEndAsync();

                var (title, cards) = ParseExport(content);
                if (cards.Count == 0)
                {
                    await Application.Current?.MainPage?.DisplayAlert("Import", "No cards found in file.", "OK")!;
                    return;
                }

                await Navigator.PushAsync(new ImportPage(title, cards), nav);
            }
            catch (Exception ex)
            {
                await Application.Current?.MainPage?.DisplayAlert("Import Failed", ex.Message, "OK")!;
            }
        };

        // Settings -> open ProfileSettingsPage
        menu.SettingsTapped += async (_, __) =>
        {
            if (Shell.Current is not null)
                await Navigator.GoToAsync(nameof(ProfileSettingsPage));
            else
                await Navigator.PushAsync(new ProfileSettingsPage(), nav);
        };
    }

    static (string Title, List<(string Q, string A)> Cards) ParseExport(string content)
    {
        var lines = content.Replace("\r", string.Empty).Split('\n');
        string title = lines.FirstOrDefault(l => l.StartsWith("Reviewer:", StringComparison.OrdinalIgnoreCase))?.Substring(9).Trim() ?? "Imported Reviewer";
        var cards = new List<(string Q, string A)>();
        string? q = null;
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("Q:", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(q)) { cards.Add((q, string.Empty)); }
                q = line.Substring(2).Trim();
            }
            else if (line.StartsWith("A:", StringComparison.OrdinalIgnoreCase))
            {
                var a = line.Substring(2).Trim();
                if (!string.IsNullOrWhiteSpace(q) || !string.IsNullOrWhiteSpace(a))
                {
                    cards.Add((q ?? string.Empty, a));
                    q = null;
                }
            }
        }
        if (!string.IsNullOrWhiteSpace(q)) cards.Add((q, string.Empty));
        return (title, cards);
    }
}
