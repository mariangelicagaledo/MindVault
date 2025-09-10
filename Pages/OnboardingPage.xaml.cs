using System.Collections.ObjectModel;
using mindvault.Services;

namespace mindvault.Pages;

public partial class OnboardingPage : ContentPage
{
    // NOTE: Icon is a Font Awesome glyph (e.g., "\uf02d"), not a filename.
    public ObservableCollection<OnboardingSlide> Slides { get; } = new();

    public OnboardingPage()
    {
        InitializeComponent();
        BindingContext = this;

        // Use FA Free Solid glyphs
        Slides.Add(new("Flashcards",
            "Turn your notes into flashcards for better memorization.",
            "\uf02d"));   // fa-book

        Slides.Add(new("Export & Import",
            "Easily share and transfer your reviewers between devices.",
            "\uf362"));   // fa-right-left (exchange)

        Slides.Add(new("Offline Access",
            "Keep learning anytime without internet connectivity.",
            "\uf1eb"));   // fa-wifi  (use this; wifi-slash may be Pro)

        Slides.Add(new("Spaced Repetition",
            "Strengthen memory with scientifically proven review cycles.",
            "\uf1da"));   // fa-history (circular arrow)

        UpdateButtons();
    }

    void OnPositionChanged(object sender, PositionChangedEventArgs e) => UpdateButtons();

    void UpdateButtons()
    {
        bool isLast = Carousel.Position >= Slides.Count - 1;
        NextBtn.IsVisible   = !isLast;
        LetsGoBtn.IsVisible = isLast;
        SkipBtn.IsVisible   = !isLast;
    }

    void OnNext(object sender, EventArgs e)
    {
        if (Carousel.Position < Slides.Count - 1)
            Carousel.Position++;
        UpdateButtons();
    }

    async void OnSkip(object sender, EventArgs e)
    {
        // Mark onboarding as done and proceed to profile setup first
        OnboardingState.IsCompleted = true;
        await Shell.Current.GoToAsync("///SetProfilePage");
    }

    async void OnLetsGo(object sender, EventArgs e)
    {
        // Mark onboarding as done and proceed to profile setup first
        OnboardingState.IsCompleted = true;
        await Shell.Current.GoToAsync("///SetProfilePage");
    }
}

// IMPORTANT: change third field name to Icon (not Image)
public record OnboardingSlide(string Title, string Subtitle, string Icon);
