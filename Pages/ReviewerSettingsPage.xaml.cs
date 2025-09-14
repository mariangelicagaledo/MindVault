using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using Microsoft.Maui.Storage;

namespace mindvault.Pages;

public partial class ReviewerSettingsPage : ContentPage
{
    public string ReviewerTitle { get; }

    const string PrefRoundSize = "RoundSize";
    const string PrefStudyMode = "StudyMode"; // "Default" or "Exam"
    const string PrefReviewStatePrefix = "ReviewState_"; // matches CourseReviewPage

    int _roundSize;

    public ReviewerSettingsPage(string reviewerTitle = "Math Reviewer")
    {
        InitializeComponent();
        ReviewerTitle = reviewerTitle;
        _roundSize = Preferences.Get(PrefRoundSize, 10);
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        UpdateChipUI();
        UpdateModeUI(Preferences.Get(PrefStudyMode, "Default"));
    }

    void UpdateChipUI()
    {
        var chipStyle = (Style)Resources["Chip"]; var selStyle = (Style)Resources["ChipSelected"];
        var chip10 = this.FindByName<Border>("Round10Chip");
        var chip20 = this.FindByName<Border>("Round20Chip");
        var chip30 = this.FindByName<Border>("Round30Chip");
        var chip40 = this.FindByName<Border>("Round40Chip");
        if (chip10 != null) chip10.Style = chipStyle;
        if (chip20 != null) chip20.Style = chipStyle;
        if (chip30 != null) chip30.Style = chipStyle;
        if (chip40 != null) chip40.Style = chipStyle;
        Border? sel = _roundSize switch
        {
            10 => chip10,
            20 => chip20,
            30 => chip30,
            40 => chip40,
            _ => null
        };
        if (sel != null) sel.Style = selStyle;
    }

    void UpdateModeUI(string mode)
    {
        var defaultTile = this.FindByName<Border>("DefaultTile");
        var examTile = this.FindByName<Border>("ExamTile");
        if (defaultTile == null || examTile == null) return;
        if (mode == "Exam")
        {
            defaultTile.BackgroundColor = Colors.LightGray;
            examTile.BackgroundColor = (Color)Resources["Primary"]; // highlight exam
            var examLabel = this.FindByName<Label>("ExamTileLabel");
            if (examLabel != null) examLabel.TextColor = Colors.White;
            var defLabel = this.FindByName<Label>("DefaultTileLabel");
            if (defLabel != null) defLabel.TextColor = Colors.Black;
        }
        else
        {
            defaultTile.BackgroundColor = (Color)Resources["Primary"]; // highlight default
            examTile.BackgroundColor = Colors.LightGray;
            var defLabel = this.FindByName<Label>("DefaultTileLabel");
            if (defLabel != null) defLabel.TextColor = Colors.White;
            var examLabel = this.FindByName<Label>("ExamTileLabel");
            if (examLabel != null) examLabel.TextColor = Colors.Black;
        }
    }

    private void OnDefaultModeTapped(object? sender, TappedEventArgs e)
    {
        Preferences.Set(PrefStudyMode, "Default");
        UpdateModeUI("Default");
        MessagingCenter.Send(this, "StudyModeChanged", "Default");
    }

    private void OnExamModeTapped(object? sender, TappedEventArgs e)
    {
        Preferences.Set(PrefStudyMode, "Exam");
        UpdateModeUI("Exam");
        MessagingCenter.Send(this, "StudyModeChanged", "Exam");
    }

    private void OnRoundSizeTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is string s && int.TryParse(s, out var n))
        {
            _roundSize = n;
            Preferences.Set(PrefRoundSize, _roundSize);
            UpdateChipUI();
            MessagingCenter.Send(this, "RoundSizeChanged", _roundSize);
        }
    }

    private async void OnResetProgressTapped(object? sender, TappedEventArgs e)
    {
        var confirm = await this.DisplayAlert("Reset Progress", "This will erase your review progress for this course. Continue?", "Reset", "Cancel");
        if (!confirm) return;

        // We need the reviewer id to clear its saved state; try to resolve by title
        try
        {
            var db = ServiceHelper.GetRequiredService<DatabaseService>();
            var reviewers = await db.GetReviewersAsync();
            var match = reviewers.FirstOrDefault(r => r.Title == ReviewerTitle);
            if (match != null)
            {
                Preferences.Remove(PrefReviewStatePrefix + match.Id);
                MessagingCenter.Send(this, "ProgressReset", match.Id);
                await this.DisplayAlert("Progress Reset", "Your review progress has been cleared.", "OK");
            }
            else
            {
                await this.DisplayAlert("Not Found", "Could not resolve the current course.", "OK");
            }
        }
        catch (Exception ex)
        {
            await this.DisplayAlert("Reset Failed", ex.Message, "OK");
        }
    }

    private async void OnCloseTapped(object? sender, EventArgs e)
    {
        Debug.WriteLine($"[ReviewerSettingsPage] CloseSettingsToReviewers() -> ReviewersPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseSettingsToReviewers(),
            "Could not return to reviewers");
    }
}
