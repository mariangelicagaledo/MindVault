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

    public ReviewerEditorPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    async void LoadCardsAsync()
    {
        if (ReviewerId <= 0) return;
        Items.Clear();
        var cards = await _db.GetFlashcardsAsync(ReviewerId);
        foreach (var c in cards)
        {
            Items.Add(new ReviewItem
            {
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
            // Determine the ReviewItem in context
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
    { if (sender is not Element el || el.BindingContext is not ReviewItem item) return; Items.Remove(item); RenumberSaved(); await SaveAllAsync(); }
    private async void OnAddNewTapped(object? sender, TappedEventArgs e)
    { var editing = Items.LastOrDefault(x => !x.IsSaved); if (editing is not null) { editing.IsSaved = true; RenumberSaved(); await SaveAllAsync(); } Items.Add(new ReviewItem()); }
    private async void OnCardTapped(object? sender, TappedEventArgs e)
    { if (sender is not Element el || el.BindingContext is not ReviewItem item) return; var editing = Items.FirstOrDefault(x => !x.IsSaved); if (editing is not null) { editing.IsSaved = true; RenumberSaved(); await SaveAllAsync(); } item.IsSaved = false; }

    // Check icon navigation handler
    private async void OnCheckTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewerEditorPage] CloseEditorToReviewers() -> ReviewersPage");

        // Finalize any in-progress (unsaved) cards that have content
        bool changed = false;
        foreach (var it in Items.Where(x => !x.IsSaved).ToList())
        { bool hasContent = !string.IsNullOrWhiteSpace(it.Question) || !string.IsNullOrWhiteSpace(it.Answer) || it.QuestionImageVisible || it.AnswerImageVisible; if (hasContent) { it.IsSaved = true; changed = true; } }
        if (changed) RenumberSaved();

        await SaveAllAsync();
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseEditorToReviewers(), "Could not return to reviewers");
    }

    async Task SaveAllAsync()
    {
        if (ReviewerId <= 0) return;
        // Simple replace-all strategy for now
        await _db.DeleteFlashcardsForReviewerAsync(ReviewerId);
        int order = 1;
        foreach (var it in Items.Where(x => x.IsSaved))
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

    // Assign sequential numbers to saved cards
    private void RenumberSaved()
    { int i = 1; foreach (var it in Items.Where(x => x.IsSaved)) it.Number = i++; }

    // === Simple model ===
    public class ReviewItem : INotifyPropertyChanged
    {
        string _question = string.Empty;
        string _answer = string.Empty;
        string _qImg = string.Empty;
        string _aImg = string.Empty;
        bool _isSaved;
        int _number;

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