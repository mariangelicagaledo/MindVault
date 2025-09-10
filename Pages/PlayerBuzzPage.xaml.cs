using System.Collections.ObjectModel;

namespace mindvault.Pages;

public partial class PlayerBuzzPage : ContentPage
{
    public ObservableCollection<Score> Scores { get; } = new();

    public PlayerBuzzPage()
    {
        InitializeComponent();
        BindingContext = this;

        // Demo leaderboard - uses avatars under Resources/Images/
        Scores.Add(new Score { Rank = 1, Name = "kalabaw", Points = 5, Image = "avatar1.png", IsLeader = true });
        Scores.Add(new Score { Rank = 2, Name = "player2", Points = 3, Image = "avatar2.png" });
        Scores.Add(new Score { Rank = 3, Name = "player3", Points = 2, Image = "avatar3.png" });
    }

    // Bell "click" animation: quick press + bounce
    private async Task RingBellAsync()
    {
        try
        {
            await Bell.ScaleTo(0.92, 90, Easing.CubicIn);
            await Task.WhenAll(
                Bell.ScaleTo(1.0, 180, Easing.SpringOut),
                Bell.TranslateTo(0, 0, 160, Easing.SpringOut),
                Bell.RotateTo(0, 160, Easing.SpringOut)
            );
        }
        catch { /* ignore if page changed */ }
    }

    private void OnBellTapped(object? sender, TappedEventArgs e)
    {
        _ = RingBellAsync(); // Fire and forget
    }

    private async void OnAnswerTapped(object? sender, TappedEventArgs e)
    {
        // Make the bell ring when answer button is tapped
        await RingBellAsync();
    }

    public class Score
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public int Points { get; set; }
        public string PointsText => $"{Points} PTS";
        public string Image { get; set; } = "avatar1.png";
        public bool IsLeader { get; set; }
    }
}
