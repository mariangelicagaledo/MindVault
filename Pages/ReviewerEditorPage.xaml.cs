using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Globalization;
using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;
using Microsoft.Maui.Storage;

namespace mindvault.Pages;

[QueryProperty(nameof(ReviewerId), "id")]
[QueryProperty(nameof(ReviewerTitle), "title")]
public partial class ReviewerEditorPage : ContentPage, INotifyPropertyChanged
{
    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();

    const int MinCards = 5; // required minimum contentful cards per deck

    int _reviewerId;
    public int ReviewerId
    {
        get => _reviewerId;
        set { if (_reviewerId == value) return; _reviewerId = value; OnPropertyChanged(); LoadCardsAsync(); }
    }

    string _reviewerTitle = string.Empty;
    public string ReviewerTitle
    {
        get => _reviewerTitle;
        set { if (_reviewerTitle == value) return; _reviewerTitle = value ?? string.Empty; OnPropertyChanged(); }
    }

    public ObservableCollection<ReviewItem> Items { get; } = new();

    bool _allowNavigationOnce; // set when we already handled warning & are programmatically navigating

    public ReviewerEditorPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Shell.Current is not null)
            Shell.Current.Navigating += OnShellNavigating; // global guard
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (Shell.Current is not null)
            Shell.Current.Navigating -= OnShellNavigating;
    }

    async void OnShellNavigating(object? sender, ShellNavigatingEventArgs e)
    {
        try
        {
            if (_allowNavigationOnce) { _allowNavigationOnce = false; return; }
            // Only guard if current page is this editor and target is different page
            if (Shell.Current?.CurrentPage != this) return;
            // Ignore internal self-navigation
            if (e.Target.Location.OriginalString.Contains(nameof(ReviewerEditorPage))) return;

            int contentful = Items.Count(i => HasContent(i));
            if (contentful >= MinCards)
            {
                // Save deck silently then allow navigation
                await SaveAllAsync();
                return;
            }

            // Cancel navigation and prompt
            e.Cancel();
            bool leave = await DisplayAlert("Incomplete Deck", $"This deck has only {contentful} card(s). Leaving will DELETE the deck. Continue?", "Delete & Exit", "Stay");
            if (!leave) return;

            // Delete and then navigate manually to the original target
            try
            {
                await EnsureReviewerIdAsync();
                if (ReviewerId > 0)
                    await _db.DeleteReviewerCascadeAsync(ReviewerId);
            }
            catch { }
            _allowNavigationOnce = true; // allow next navigation
            await Shell.Current.GoToAsync(e.Target.Location, true);
        }
        catch { }
    }

    async Task EnsureReviewerIdAsync()
    {
        if (ReviewerId > 0) return;
        if (string.IsNullOrWhiteSpace(ReviewerTitle)) return;
        try
        {
            var reviewers = await _db.GetReviewersAsync();
            var match = reviewers.FirstOrDefault(r => r.Title == ReviewerTitle);
            if (match is not null) ReviewerId = match.Id;
        }
        catch { }
    }

    async void LoadCardsAsync()
    {
        await EnsureReviewerIdAsync();
        if (ReviewerId <= 0) return;
        Items.Clear();
        var cards = await _db.GetFlashcardsAsync(ReviewerId);
        foreach (var c in cards)
        {
            Items.Add(new ReviewItem
            {
                Id = c.Id,
                Question = c.Question,
                Answer = c.Answer,
                QuestionImagePath = c.QuestionImagePath,
                AnswerImagePath = c.AnswerImagePath,
                IsSaved = true,
                Number = c.Order
            });
        }
        RenumberSaved();
        if (Items.Count == 0) Items.Add(new ReviewItem());
    }

    // === Title rename ===
    void OnEditTitleTapped(object? sender, TappedEventArgs e)
    { RenameEntry.Text = ReviewerTitle; RenameOverlay.IsVisible = true; }
    void OnRenameCancel(object? sender, EventArgs e) => RenameOverlay.IsVisible = false;
    async void OnRenameSave(object? sender, EventArgs e)
    {
        var newTitle = RenameEntry.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(newTitle)) { await DisplayAlert("Invalid", "Please enter a valid title.", "OK"); return; }
        try { if (ReviewerId > 0) await _db.UpdateReviewerTitleAsync(ReviewerId, newTitle); ReviewerTitle = newTitle; }
        catch (Exception ex) { await DisplayAlert("Rename Failed", ex.Message, "OK"); return; }
        finally { RenameOverlay.IsVisible = false; }
    }

    // Pick image for the currently editing item
    async void OnPickImageTapped(object? sender, TappedEventArgs e)
    {
        try
        {
            if (sender is not Element el) return;
            var ctx = el.BindingContext as ReviewItem ?? (el.Parent as Element)?.BindingContext as ReviewItem;
            if (ctx is null) return;
            var side = (e as TappedEventArgs)?.Parameter as string;

            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[] { "image/*" } },
                { DevicePlatform.iOS, new[] { "public.image" } },
                { DevicePlatform.MacCatalyst, new[] { "public.image" } },
                { DevicePlatform.WinUI, new[] { ".png", ".jpg", ".jpeg" } },
            });
            var pick = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Select image", FileTypes = fileTypes });
            if (pick is null) return;

            var ext = Path.GetExtension(pick.FileName);
            var dest = Path.Combine(FileSystem.AppDataDirectory, $"card_{Guid.NewGuid():N}{ext}");
            using (var src = await pick.OpenReadAsync())
            using (var dst = File.Create(dest))
                await src.CopyToAsync(dst);

            if (string.Equals(side, "A", StringComparison.OrdinalIgnoreCase))
                ctx.AnswerImagePath = dest;
            else
                ctx.QuestionImagePath = dest;

            ctx.OnPropertyChanged(nameof(ReviewItem.QuestionImagePath));
            ctx.OnPropertyChanged(nameof(ReviewItem.AnswerImagePath));
            ctx.OnPropertyChanged(nameof(ReviewItem.QuestionImageVisible));
            ctx.OnPropertyChanged(nameof(ReviewItem.AnswerImageVisible));
        }
        catch (Exception ex)
        {
            await DisplayAlert("Image", ex.Message, "OK");
        }
    }

    // === UI events from XAML ===
    private async void OnSaveTapped(object? sender, TappedEventArgs e)
    { if (sender is not Element el || el.BindingContext is not ReviewItem item) return; item.IsSaved = true; RenumberSaved(); await SaveAllAsync(); }
    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not ReviewItem item) return;
        var confirm = await DisplayAlert("Delete Card", "Are you sure you want to delete this card?", "Delete", "Cancel");
        if (!confirm) return;

        int currentSaved = Items.Count(i => i.IsSaved && HasContent(i));
        int delta = (item.IsSaved && HasContent(item)) ? 1 : 0;
        if (currentSaved - delta < MinCards)
        {
            await DisplayAlert("Minimum Cards", $"Deleting this would leave fewer than {MinCards} cards (deck will be deleted if you exit without adding more).", "OK");
        }

        await EnsureReviewerIdAsync();
        Items.Remove(item);
        RenumberSaved();
        await SaveAllAsync();
        await PageHelpers.SafeDisplayAlertAsync(this, "Deleted", "Card removed.");
    }
    private async void OnAddNewTapped(object? sender, TappedEventArgs e)
    { var editing = Items.LastOrDefault(x => !x.IsSaved); if (editing is not null) { editing.IsSaved = true; RenumberSaved(); await SaveAllAsync(); } Items.Add(new ReviewItem()); }
    private async void OnCardTapped(object? sender, TappedEventArgs e)
    { if (sender is not Element el || el.BindingContext is not ReviewItem item) return; var editing = Items.FirstOrDefault(x => !x.IsSaved); if (editing is not null) { editing.IsSaved = true; RenumberSaved(); await SaveAllAsync(); } item.IsSaved = false; }

    // New unified exit handler for check button & hardware back
    private async Task<bool> AttemptExitAsync()
    {
        int contentful = Items.Count(i => HasContent(i));
        if (contentful < MinCards)
        {
            bool leave = await DisplayAlert("Incomplete Deck", $"This deck has only {contentful} card(s). Leaving will DELETE the deck. Continue?", "Delete & Exit", "Stay");
            if (!leave) return false;
            try
            {
                await EnsureReviewerIdAsync();
                if (ReviewerId > 0)
                    await _db.DeleteReviewerCascadeAsync(ReviewerId);
            }
            catch { }
            _allowNavigationOnce = true;
            await NavigationService.CloseEditorToReviewers();
            return true;
        }

        bool changed = false;
        foreach (var it in Items.Where(x => !x.IsSaved).ToList())
        {
            if (HasContent(it)) { it.IsSaved = true; changed = true; }
        }
        if (changed) RenumberSaved();
        await SaveAllAsync();
        _allowNavigationOnce = true;
        await NavigationService.CloseEditorToReviewers();
        return true;
    }

    private async void OnCheckTapped(object? sender, EventArgs e)
    {
        await AttemptExitAsync();
    }

    protected override bool OnBackButtonPressed()
    {
        _ = AttemptExitAsync();
        return true; // we handle navigation
    }

    async Task SaveAllAsync()
    {
        await EnsureReviewerIdAsync();
        if (ReviewerId <= 0) return;

        var saved = Items.Where(x => x.IsSaved && HasContent(x)).ToList();
        if (saved.Count == 0) return; // nothing to save (may later delete on exit)

        await _db.DeleteFlashcardsForReviewerAsync(ReviewerId);
        int order = 1;
        foreach (var it in saved)
        {
            var card = new Flashcard
            {
                ReviewerId = ReviewerId,
                Question = it.Question?.Trim() ?? string.Empty,
                Answer = it.Answer?.Trim() ?? string.Empty,
                QuestionImagePath = it.QuestionImagePath ?? string.Empty,
                AnswerImagePath = it.AnswerImagePath ?? string.Empty,
                Learned = false,
                Order = order++
            };
            await _db.AddFlashcardAsync(card);
        }
    }

    static bool HasContent(ReviewItem it)
    {
        return !string.IsNullOrWhiteSpace(it.Question)
            || !string.IsNullOrWhiteSpace(it.Answer)
            || it.QuestionImageVisible
            || it.AnswerImageVisible;
    }

    private void RenumberSaved()
    { int i = 1; foreach (var it in Items.Where(x => x.IsSaved)) it.Number = i++; }

    public class ReviewItem : INotifyPropertyChanged
    {
        int _id;
        string _question = string.Empty;
        string _answer = string.Empty;
        string _qImg = string.Empty;
        string _aImg = string.Empty;
        bool _isSaved;
        int _number;

        public int Id { get => _id; set { if (_id == value) return; _id = value; OnPropertyChanged(); } }
        public string Question { get => _question; set { if (_question == value) return; _question = value ?? string.Empty; OnPropertyChanged(); } }
        public string Answer { get => _answer; set { if (_answer == value) return; _answer = value ?? string.Empty; OnPropertyChanged(); } }
        public string QuestionImagePath { get => _qImg; set { if (_qImg == value) return; _qImg = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(QuestionImageVisible)); } }
        public string AnswerImagePath { get => _aImg; set { if (_aImg == value) return; _aImg = value ?? string.Empty; OnPropertyChanged(); OnPropertyChanged(nameof(AnswerImageVisible)); } }
        public bool QuestionImageVisible => !string.IsNullOrWhiteSpace(QuestionImagePath);
        public bool AnswerImageVisible => !string.IsNullOrWhiteSpace(AnswerImagePath);

        public bool IsSaved { get => _isSaved; set { if (_isSaved == value) return; _isSaved = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEditing)); } }
        public bool IsEditing => !_isSaved;

        public int Number { get => _number; set { if (_number == value) return; _number = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        public void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}