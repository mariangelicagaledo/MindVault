using System.Collections.ObjectModel;
using mindvault.Services;

namespace mindvault.Pages;

public partial class GameOverPage : ContentPage
{
    public string DeckTitle { get; }
    public ObservableCollection<Row> Rows { get; } = new();
    public List<string> Winners { get; }

    private readonly MultiplayerService _multi = Services.ServiceHelper.GetRequiredService<MultiplayerService>();

    public GameOverPage(MultiplayerService.GameOverPayload payload)
    {
        InitializeComponent();

        DeckTitle = payload.DeckTitle;
        Winners = payload.Winners;

        // Build leaderboard rows with avatar support when available
        int rank = 1;
        foreach (var s in payload.FinalScores.OrderByDescending(s => s.score))
        {
            Rows.Add(new Row
            {
                Rank = rank,
                Name = string.IsNullOrEmpty(s.name) ? s.id : s.name,
                Points = s.score,
                Image = ResolveAvatarFor(s.id)
            });
            rank++;
        }

        BindingContext = this;

        // React immediately if host leaves while on Game Over screen
        _multi.ClientHostLeft += OnClientHostLeft;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _multi.ClientHostLeft -= OnClientHostLeft;
    }

    private void OnClientHostLeft()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try { await DisplayAlert("Host", "The host has left the game.", "OK"); } catch { }
            _multi.DisconnectClient();
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync("//HomePage");
            else
                await Navigation.PopToRootAsync();
        });
    }

    private string ResolveAvatarFor(string id)
    {
        // Try to resolve from multiplayer cached participants if available
        try
        {
            var list = _multi.GetClientParticipantsSnapshot();
            var p = list.FirstOrDefault(x => x.Id == id);
            return string.IsNullOrEmpty(p?.Avatar) ? "avatar1.png" : p!.Avatar;
        }
        catch { return "avatar1.png"; }
    }

    public class Row
    {
        public int Rank { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Points { get; set; }
        public string PointsText => $"{Points} pts";
        public string Image { get; set; } = "avatar1.png";
    }

    private async void OnLobbyClicked(object? sender, EventArgs e)
    {
        if (_multi.IsHosting)
        {
            // Keep host running and go back to host lobby
            var lobby = new HostLobbyPage(_multi.CurrentRoomCode ?? string.Empty);
            Navigation.InsertPageBefore(lobby, this);
            await Navigation.PopAsync();
        }
        else
        {
            // Client stays connected; go to client lobby
            var lobby = new PlayerLobbyPage();
            Navigation.InsertPageBefore(lobby, this);
            await Navigation.PopAsync();
        }
    }

    private async void OnExitClicked(object? sender, EventArgs e)
    {
        if (_multi.IsHosting)
        {
            // Host: end hosting and close all sessions
            _multi.StopHosting();
        }
        else
        {
            // Client: cleanly disconnect
            try { await _multi.SendLeaveAsync(); } catch { }
            _multi.DisconnectClient();
        }

        // Back to home/root
        await Navigation.PopToRootAsync();
    }
}
