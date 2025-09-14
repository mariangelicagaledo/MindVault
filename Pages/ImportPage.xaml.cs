using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;
using Microsoft.Maui.Storage;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using mindvault.Models;

namespace mindvault.Pages;

public partial class ImportPage : ContentPage
{
    // Preview data
    public string ReviewerTitle { get; private set; }
    public int Questions => Cards?.Count ?? 0;
    public string QuestionsText => Questions.ToString();

    public ObservableCollection<CardPreview> Cards { get; private set; } = new();

    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();

    // Preview ctor
    public ImportPage(string reviewerTitle, List<(string Q, string A)> cards)
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        foreach (var c in cards)
            Cards.Add(new CardPreview { Question = c.Q, Answer = c.A });
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
    }

    // Legacy/demo ctor kept (not used in new flow)
    public ImportPage(string reviewerTitle = "Science Reviewer", int questions = 75)
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this, "Burger", "MainMenu");
    }

    private async void OnImportTapped(object? sender, EventArgs e)
    {
        try
        {
            if (Cards is null || Cards.Count == 0)
            {
                await PageHelpers.SafeDisplayAlertAsync(this, "Import", "No preview data to import.", "OK");
                return;
            }

            var finalTitle = await EnsureUniqueTitleAsync(ReviewerTitle);
            var reviewer = new Reviewer { Title = finalTitle, CreatedUtc = DateTime.UtcNow };
            await _db.AddReviewerAsync(reviewer);

            int order = 1;
            foreach (var c in Cards)
            {
                await _db.AddFlashcardAsync(new Flashcard
                {
                    ReviewerId = reviewer.Id,
                    Question = c.Question,
                    Answer = c.Answer,
                    Learned = false,
                    Order = order++
                });
            }

            await PageHelpers.SafeDisplayAlertAsync(this, "Import", $"Imported '{finalTitle}' with {Cards.Count} cards.", "OK");
            await NavigationService.Back();
        }
        catch (Exception ex)
        {
            await PageHelpers.SafeDisplayAlertAsync(this, "Import Failed", ex.Message, "OK");
        }
    }

    private async Task<string> EnsureUniqueTitleAsync(string title)
    {
        var existing = await _db.GetReviewersAsync();
        if (!existing.Any(r => string.Equals(r.Title, title, StringComparison.OrdinalIgnoreCase)))
            return title;
        int i = 2;
        while (true)
        {
            var candidate = $"{title} ({i})";
            if (!existing.Any(r => string.Equals(r.Title, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
            i++;
        }
    }
}
