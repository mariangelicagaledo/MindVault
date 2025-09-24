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

        var snapshot = _multi.GetClientParticipantsSnapshot();
        int rank = 1;
        foreach (var s in payload.FinalScores.OrderByDescending(s => s.Score))
        {
            var name = s.Name;
            var avatar = s.Avatar;
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(avatar))
            {
                var p = snapshot.FirstOrDefault(x => x.Id == s.Id);
                if (string.IsNullOrEmpty(name)) name = p?.Name ?? (string.Equals(s.Id, _multi.SelfId, StringComparison.Ordinal) ? ProfileState.Name : s.Id);
                if (string.IsNullOrEmpty(avatar)) avatar = p?.Avatar ?? (string.Equals(s.Id, _multi.SelfId, StringComparison.Ordinal) ? ProfileState.Avatar : "avatar1.png");
            }

            Rows.Add(new Row
            {
                Rank = rank,
                Name = string.IsNullOrEmpty(name) ? s.Id : name,
                Points = s.Score,
                Image = string.IsNullOrEmpty(avatar) ? "avatar1.png" : avatar
            });
            rank++;
        }

        BindingContext = this;
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
            var lobby = new HostLobbyPage(_multi.CurrentRoomCode ?? string.Empty);
            Navigation.InsertPageBefore(lobby, this);
            await Navigation.PopAsync();
        }
        else
        {
            var lobby = new PlayerLobbyPage();
            Navigation.InsertPageBefore(lobby, this);
            await Navigation.PopAsync();
        }
    }

    private async void OnExitClicked(object? sender, EventArgs e)
    {
        if (_multi.IsHosting)
        {
            _multi.StopHosting();
        }
        else
        {
            try { await _multi.SendLeaveAsync(); } catch { }
            _multi.DisconnectClient();
        }

        await Navigation.PopToRootAsync();
    }
}
