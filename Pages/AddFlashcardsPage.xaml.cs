using mindvault.Services;
using mindvault.Utils;
using mindvault.Data;
using Microsoft.Maui.Storage;
using System.Text;

namespace mindvault.Pages;

[QueryProperty(nameof(ReviewerId), "id")]
[QueryProperty(nameof(ReviewerTitle), "title")]
public partial class AddFlashcardsPage : ContentPage
{
    public int ReviewerId { get; set; }
    public string ReviewerTitle { get; set; } = string.Empty;

    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();
    const int MinCards = 5; // minimum required to keep deck
    bool _navigatingForward; // suppress deletion when moving deeper in creation flow

    public AddFlashcardsPage()
    {
        InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        DeckTitleLabel.Text = $"Deck: {ReviewerTitle}";
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _navigatingForward = false; // reset when page shown again
    }

    protected override async void OnDisappearing()
    {
        base.OnDisappearing();
        // If user leaves this page (not navigating forward into add flows) and deck has < MinCards, delete it.
        if (_navigatingForward) return;
        try
        {
            if (ReviewerId > 0)
            {
                var cards = await _db.GetFlashcardsAsync(ReviewerId);
                if (cards.Count < MinCards)
                {
                    await _db.DeleteReviewerCascadeAsync(ReviewerId);
                }
            }
        }
        catch { }
    }

    void OnBack(object? sender, TappedEventArgs e) => Navigation.PopAsync();

    // X close -> return to TitleReviewerPage (deck title input). Keep _navigatingForward = false so cleanup can run.
    async void OnClose(object? sender, TappedEventArgs e)
    {
        try { await Shell.Current.GoToAsync("///TitleReviewerPage"); } catch { await Navigation.PopAsync(); }
    }

    async void OnTypeFlashcards(object? sender, TappedEventArgs e)
    {
        _navigatingForward = true;
        await PageHelpers.SafeNavigateAsync(this,
            async () => await Shell.Current.GoToAsync($"///ReviewerEditorPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}"),
            "Could not open editor");
    }

    async void OnImportPaste(object? sender, TappedEventArgs e)
    {
        try
        {
            if (ReviewerId <= 0)
            {
                await DisplayAlert("Import", "Reviewer not created yet.", "OK");
                return;
            }

            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "text/plain" } },
                { DevicePlatform.iOS, new[] { "public.plain-text" } },
                { DevicePlatform.MacCatalyst, new[] { "public.plain-text" } },
                { DevicePlatform.WinUI, new[] { ".txt" } },
            });

            var pick = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Select export file (.txt)",
                FileTypes = fileTypes
            });
            if (pick is null) return;

            string content;
            using (var stream = await pick.OpenReadAsync())
            using (var reader = new StreamReader(stream))
                content = await reader.ReadToEndAsync();

            var (_, cards) = ParseExport(content);
            if (cards.Count == 0)
            {
                await DisplayAlert("Import", "No cards found in file.", "OK");
                return;
            }

            // Append cards to this reviewer
            var existing = await _db.GetFlashcardsAsync(ReviewerId);
            int order = existing.Count + 1;
            foreach (var c in cards)
            {
                await _db.AddFlashcardAsync(new Flashcard
                {
                    ReviewerId = ReviewerId,
                    Question = c.Q,
                    Answer = c.A,
                    Learned = false,
                    Order = order++
                });
            }
            await DisplayAlert("Import", $"Added {cards.Count} cards to '{ReviewerTitle}'.", "OK");
            _navigatingForward = true;
            // Navigate to editor to view
            await Shell.Current.GoToAsync($"///ReviewerEditorPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}");
        }
        catch (Exception ex)
        {
            await DisplayAlert("Import Failed", ex.Message, "OK");
        }
    }

    static (string Title, List<(string Q, string A)> Cards) ParseExport(string content)
    {
        var lines = content.Replace("\r", string.Empty).Split('\n');
        string title = lines.FirstOrDefault(l => l.StartsWith("Reviewer:", StringComparison.OrdinalIgnoreCase))?.Substring(9).Trim() ?? "Imported";
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

    async void OnSummarize(object? sender, TappedEventArgs e)
    {
        _navigatingForward = true;
        await PageHelpers.SafeNavigateAsync(this,
            async () => await Shell.Current.GoToAsync($"///SummarizeContentPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}"),
            "Could not open summarize page");
    }
}
