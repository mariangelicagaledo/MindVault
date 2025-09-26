using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;

namespace mindvault.Pages;

public partial class TitleReviewerPage : ContentPage
{
    readonly DatabaseService _db;
    bool _isCreating;

    public TitleReviewerPage()
    {
        InitializeComponent();
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
        _db = ServiceHelper.GetRequiredService<DatabaseService>();
    }

    private async void OnCreateNewTapped(object sender, EventArgs e)
    {
        if (_isCreating) return;
        _isCreating = true;
        try
        {
            var title = TitleEntry?.Text?.Trim();
            if (string.IsNullOrEmpty(title))
            {
                await PageHelpers.SafeDisplayAlertAsync(this, "Oops", "Please enter a title for your reviewer.", "OK");
                return;
            }

            // Create reviewer row
            var reviewer = new Reviewer { Title = title };
            await _db.AddReviewerAsync(reviewer);

            Debug.WriteLine($"[TitleReviewerPage] Created reviewer #{reviewer.Id} '{reviewer.Title}' -> AddFlashcardsPage");

            // Navigate to editor, passing id and title (single-shot)
            await PageHelpers.SafeNavigateAsync(this,
                async () => await Shell.Current.GoToAsync($"///AddFlashcardsPage?id={reviewer.Id}&title={Uri.EscapeDataString(reviewer.Title)}"),
                "Could not open add flashcards page");
        }
        finally
        {
            _isCreating = false;
        }
    }
}