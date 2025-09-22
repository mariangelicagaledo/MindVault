using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mindvault.Services;

namespace mindvault.Pages;

public partial class PlayerLobbyPage : ContentPage, INotifyPropertyChanged
{
    public ObservableCollection<Participant> Participants { get; } = new();

    public string ParticipantsHeader => $"Participants: {Participants.Count}/8";

    private readonly MultiplayerService _multi = Services.ServiceHelper.GetRequiredService<MultiplayerService>();

    private bool _localReady;
    public bool LocalReady
    {
        get => _localReady;
        set { if (_localReady == value) return; _localReady = value; OnPropertyChanged(); }
    }

    public PlayerLobbyPage()
    {
        InitializeComponent();
        BindingContext = this;

        // subscribe to multiplayer events
        _multi.ClientParticipantJoined += OnClientParticipantJoined;
        _multi.ClientParticipantLeft += OnClientParticipantLeft;
        _multi.ClientParticipantReadyChanged += OnClientParticipantReadyChanged;
        _multi.ClientGameStarted += OnClientGameStarted;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Connect if not connected yet
        var (ok, error) = await _multi.ConnectToHostAsync();
        if (!ok)
        {
            await DisplayAlert("Join", error ?? "Unable to connect to host.", "OK");
            await Navigation.PopAsync();
            return;
        }

        // Send JOIN with profile info
        await _multi.SendJoinAsync(ProfileState.Name, ProfileState.Avatar);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _multi.ClientParticipantJoined -= OnClientParticipantJoined;
        _multi.ClientParticipantLeft -= OnClientParticipantLeft;
        _multi.ClientParticipantReadyChanged -= OnClientParticipantReadyChanged;
        _multi.ClientGameStarted -= OnClientGameStarted;
    }

    private void OnClientParticipantJoined(MultiplayerService.ParticipantInfo p)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            // avoid duplicates
            if (!Participants.Any(x => x.Name == p.Name))
            {
                Participants.Add(new Participant { Name = p.Name, Image = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar, Ready = p.Ready });
                OnPropertyChanged(nameof(ParticipantsHeader));
            }
        });
    }

    private void OnClientParticipantLeft(string name)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Name == name);
            if (found is not null)
            {
                Participants.Remove(found);
                OnPropertyChanged(nameof(ParticipantsHeader));
            }
        });
    }

    private void OnClientParticipantReadyChanged(string name, bool ready)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Name == name);
            if (found is not null)
            {
                found.Ready = ready;
                // reflect local ready if it's us
                if (string.Equals(found.Name, ProfileState.Name, StringComparison.Ordinal))
                {
                    LocalReady = ready;
                }
            }
        });
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        _multi.DisconnectClient();
        await Navigation.PopAsync();
    }

    private async void OnReadyTapped(object? sender, TappedEventArgs e)
    {
        var newState = !LocalReady;
        await _multi.SendReadyAsync(newState);
        LocalReady = newState;
    }

    private void OnClientGameStarted()
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Navigation.PushAsync(new PlayerBuzzPage());
        });
    }

    // model
    public class Participant : INotifyPropertyChanged
    {
        private string _name = "";
        private string _image = "avatar1.png";
        private bool _ready;

        public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
        public string Image { get => _image; set { if (_image == value) return; _image = value; OnPropertyChanged(); } }
        public bool Ready { get => _ready; set { if (_ready == value) return; _ready = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
