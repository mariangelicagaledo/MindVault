using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Linq;
using System.Globalization;
using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;

namespace mindvault.Pages;

public partial class ReviewerEditorPage : ContentPage
{
    public string ReviewerTitle { get; }

    public ObservableCollection<ReviewItem> Items { get; } = new();

    public ReviewerEditorPage(string reviewerTitle = "Math Reviewer")
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);

        // Demo data to match your screenshot: one saved + one editable
        Items.Add(new ReviewItem
        {
            Question = "What is 8 + 5?",
            Answer = "13",
            IsSaved = true
        });
        Items.Add(new ReviewItem
        {
            Question = "What is 4 + 5?",
            Answer = "9",
            IsSaved = false
        });

        RenumberSaved();
    }

    // === UI events from XAML ===
    private void OnSaveTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not ReviewItem item) return;
        item.IsSaved = true;
        RenumberSaved();
    }

    private void OnDeleteTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Element el || el.BindingContext is not ReviewItem item) return;
        Items.Remove(item);
        RenumberSaved();
    }

    private void OnAddNewTapped(object? sender, TappedEventArgs e)
    {
        Items.Add(new ReviewItem()); // blank editable card
    }



    // Check icon navigation handler
    private async void OnCheckTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewerEditorPage] CloseEditorToTitle() -> TitleReviewerPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseEditorToTitle(),
            "Could not return to title page");
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

} 