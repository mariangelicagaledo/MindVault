using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Globalization;
using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using mindvault.Data;

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
        // Start empty; loaded when ReviewerId is set via query
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
                IsSaved = true,
                Number = c.Order
            });
        }
        RenumberSaved();
    }

    // === UI events from XAML ===
    private async void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not ReviewItem item) return;
        item.IsSaved = true;
        RenumberSaved();
        await SaveAllAsync();
    }

    private async void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not ReviewItem item) return;
        Items.Remove(item);
        RenumberSaved();
        await SaveAllAsync();
    }

    private async void OnAddNewTapped(object? sender, TappedEventArgs e)
    {
        Items.Add(new ReviewItem()); // blank editable card
        await Task.CompletedTask;
    }



    // Check icon navigation handler
    private async void OnCheckTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewerEditorPage] CloseEditorToReviewers() -> ReviewersPage");
        await SaveAllAsync();
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseEditorToReviewers(),
            "Could not return to reviewers");
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
                Learned = false,
                Order = order++
            };
            await _db.AddFlashcardAsync(card);
        }
    }

    // Assign sequential numbers to saved cards
    private void RenumberSaved()
    {
        int i = 1;
        foreach (var it in Items.Where(x => x.IsSaved))
            it.Number = i++;
    }

    // === Simple model ===
    public class ReviewItem : INotifyPropertyChanged
    {
        string _question = string.Empty;
        string _answer = string.Empty;
        bool _isSaved;
        int _number;

        public string Question
        {
            get => _question;
            set { if (_question == value) return; _question = value ?? string.Empty; OnPropertyChanged(); }
        }

        public string Answer
        {
            get => _answer;
            set { if (_answer == value) return; _answer = value ?? string.Empty; OnPropertyChanged(); }
        }

        public bool IsSaved
        {
            get => _isSaved;
            set { if (_isSaved == value) return; _isSaved = value; OnPropertyChanged(); OnPropertyChanged(nameof(IsEditing)); }
        }

        public int Number
        {
            get => _number;
            set { if (_number == value) return; _number = value; OnPropertyChanged(); }
        }

        public bool IsEditing
        {
            get => !_isSaved;
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}