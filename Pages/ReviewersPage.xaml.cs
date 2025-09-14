using System.Collections.ObjectModel;
using mindvault.Controls;
using mindvault.Services;
using mindvault.Pages;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;
using Microsoft.Maui.Storage;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using Microsoft.Maui.Devices;

namespace mindvault.Pages;

public partial class ReviewersPage : ContentPage
{
    // Formal, consistent dropdown options
    public ObservableCollection<string> SortOptions { get; } = new()
    {
        "All (Default)",
        "Last Played (Recent first)",
        "Alphabetical (A–Z)",
        "Alphabetical (Z–A)",
        "Created Date (Newest first)",
        "Created Date (Oldest first)"
    };

    private string _selectedSort = "Last Played (Recent first)";
    public string SelectedSort
    {
        get => _selectedSort;
        set
        {
            if (_selectedSort == value) return;
            _selectedSort = value ?? "All (Default)";
            OnPropertyChanged(nameof(SelectedSort));
            ApplySort();
        }
    }

    // Search state
    bool _isSearchVisible;
    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set { if (_isSearchVisible == value) return; _isSearchVisible = value; OnPropertyChanged(); }
    }

    string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (_searchText == value) return; _searchText = value ?? string.Empty; OnPropertyChanged(); ApplySort(); }
    }

    public ObservableCollection<ReviewerCard> Reviewers { get; } = new();

    // Keep the baseline order loaded from DB (default order)
    private List<ReviewerCard> _baseline = new();

    readonly DatabaseService _db;

    public ReviewersPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        _db = ServiceHelper.GetRequiredService<DatabaseService>();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadFromDbAsync();
        WireOnce();
    }

    async Task LoadFromDbAsync()
    {
        Reviewers.Clear();
        _baseline.Clear();
        var rows = await _db.GetReviewersAsync();
        foreach (var r in rows)
        {
            var cards = await _db.GetFlashcardsAsync(r.Id);
            var lastPlayed = Preferences.Get(GetLastPlayedKey(r.Id), DateTime.MinValue);

            var card = new ReviewerCard
            {
                Id = r.Id,
                Title = r.Title,
                Questions = cards.Count,
                LearnedRatio = (cards.Count == 0) ? 0 : (double)cards.Count(c => c.Learned) / cards.Count,
                Due = 0,
                CreatedUtc = r.CreatedUtc,
                LastPlayedUtc = lastPlayed == DateTime.MinValue ? null : lastPlayed
            };
            _baseline.Add(card);
        }
        // Fill UI list with baseline, then apply current sort
        foreach (var c in _baseline)
            Reviewers.Add(c);
        ApplySort();
    }

    static string GetLastPlayedKey(int reviewerId) => $"reviewer_last_played_{reviewerId}";

    // ===== Robust navigation wiring =====
    bool _wired;
    void WireOnce()
    {
        if (_wired) return;
        // Removed ImportPill extra wiring to avoid duplicating XAML tap handler
        _wired = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // If you add event handlers via +=, unsubscribe here to prevent leaks.
    }

    void ApplySort()
    {
        IEnumerable<ReviewerCard> source = _baseline;

        // Apply search filter first
        var keyword = SearchText?.Trim();
        if (!string.IsNullOrEmpty(keyword))
        {
            source = source.Where(c => c.Title?.Contains(keyword, StringComparison.OrdinalIgnoreCase) == true);
        }

        // Then apply sorting
        switch (SelectedSort)
        {
            case "Last Played (Recent first)":
                source = source
                    .OrderByDescending(c => c.LastPlayedUtc.HasValue)
                    .ThenByDescending(c => c.LastPlayedUtc);
                break;
            case "Alphabetical (A–Z)":
                source = source.OrderBy(c => c.Title, StringComparer.OrdinalIgnoreCase);
                break;
            case "Alphabetical (Z–A)":
                source = source.OrderByDescending(c => c.Title, StringComparer.OrdinalIgnoreCase);
                break;
            case "Created Date (Newest first)":
                source = source.OrderByDescending(c => c.CreatedUtc);
                break;
            case "Created Date (Oldest first)":
                source = source.OrderBy(c => c.CreatedUtc);
                break;
            case "All (Default)":
            default:
                // keep current source order
                break;
        }

        var result = source.ToList();
        Reviewers.Clear();
        foreach (var c in result)
            Reviewers.Add(c);
    }

    private async void OnDeleteTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            bool confirmed = await PageHelpers.SafeDisplayAlertAsync(this, "Delete Reviewer", 
                $"Are you sure you want to delete '{reviewer.Title}'?", 
                "Delete", "Cancel");
            if (confirmed)
            {
                await _db.DeleteReviewerCascadeAsync(reviewer.Id);
                _baseline.RemoveAll(x => x.Id == reviewer.Id);
                ApplySort();
                await PageHelpers.SafeDisplayAlertAsync(this, "Deleted", $"'{reviewer.Title}' has been removed.", "OK");
            }
        }
    }

    private async void OnViewCourseTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            Debug.WriteLine($"[ReviewersPage] OpenCourse() -> CourseReviewPage");
            await Navigator.PushAsync(new CourseReviewPage(reviewer.Id, reviewer.Title), Navigation);
        }
    }

    private async void OnEditTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Element el && el.BindingContext is ReviewerCard reviewer)
        {
            Debug.WriteLine($"[ReviewersPage] OpenEditor() -> ReviewerEditorPage (Id={reviewer.Id}, Title={reviewer.Title})");
            var route = $"{nameof(ReviewerEditorPage)}?id={reviewer.Id}&title={Uri.EscapeDataString(reviewer.Title)}";
            await Shell.Current.GoToAsync(route);
        }
    }

    private void OnSearchTapped(object? sender, TappedEventArgs e)
    {
        IsSearchVisible = !IsSearchVisible;
        if (IsSearchVisible)
        {
            // Try to focus the SearchBar if available
            _ = MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await Task.Delay(50);
                DeckSearchBar?.Focus();
            });
        }
        else
        {
            SearchText = string.Empty; // clearing will refresh the list
        }
    }

    // Create reviewer button handler
    private async void OnCreateReviewerTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewersPage] OpenTitle() -> TitleReviewerPage");
        await NavigationService.OpenTitle();
    }

    private async void OnExportTapped(object? sender, EventArgs e)
    {
        if (sender is Border border && border.BindingContext is ReviewerCard reviewer)
        {
            try
            {
                // Fetch flashcards for this reviewer
                var cards = await _db.GetFlashcardsAsync(reviewer.Id);
                var list = cards.Select(c => (c.Question, c.Answer)).ToList();
                // Navigate to ExportPage for preview
                await Navigator.PushAsync(new ExportPage(reviewer.Title, list), Navigation);
            }
            catch (Exception ex)
            {
                await PageHelpers.SafeDisplayAlertAsync(this, "Export", ex.Message, "OK");
            }
        }
    }

    private async void OnImportTapped(object? sender, EventArgs e)
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
                await PageHelpers.SafeDisplayAlertAsync(this, "Import", "No cards found in file.", "OK");
                return;
            }

            // Navigate to ImportPage to preview and confirm import
            await Navigator.PushAsync(new ImportPage(title, cards), Navigation);
        }
        catch (Exception ex)
        {
            await PageHelpers.SafeDisplayAlertAsync(this, "Import Failed", ex.Message, "OK");
        }
    }

    private (string Title, List<(string Q, string A)> Cards) ParseExport(string content)
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

public class ReviewerCard
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Questions { get; set; }
    /// <summary>0..1</summary>
    public double LearnedRatio { get; set; }
    public int Due { get; set; }

    public DateTime CreatedUtc { get; set; }
    public DateTime? LastPlayedUtc { get; set; }

    // Convenience texts for binding
    public string LearnedPercentText => $"{(int)(LearnedRatio * 100)}% Learned";
    public string DueText => $"{Due} due";
}
