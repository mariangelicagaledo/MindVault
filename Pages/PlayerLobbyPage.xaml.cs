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
        _multi.ClientHostLeft += OnClientHostLeft;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Connect if not connected yet (prevents duplicate connections on re-enter)
        var (ok, error) = await _multi.ConnectToHostAsync();
        if (!ok)
        {
            await DisplayAlert("Join", error ?? "Unable to connect to host.", "OK");
            await Navigation.PopAsync();
            return;
        }

        // Only send JOIN on first connect (avoid duplicates on re-enter)
        if (string.IsNullOrEmpty(_multi.SelfId))
        {
            await _multi.SendJoinAsync(ProfileState.Name, ProfileState.Avatar);
        }

        // Hydrate UI from current client snapshot to avoid duplicates
        RefreshParticipantsFromClientSnapshot();
    }

    private void RefreshParticipantsFromClientSnapshot()
    {
        var snapshot = _multi.GetClientParticipantsSnapshot();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Participants.Clear();
            foreach (var p in snapshot)
            {
                // Each participant once, including self as assigned by server
                Participants.Add(new Participant
                {
                    Id = p.Id,
                    Name = p.Name,
                    Image = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar,
                    Ready = p.Ready
                });
            }
            OnPropertyChanged(nameof(ParticipantsHeader));
        });
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _multi.ClientParticipantJoined -= OnClientParticipantJoined;
        _multi.ClientParticipantLeft -= OnClientParticipantLeft;
        _multi.ClientParticipantReadyChanged -= OnClientParticipantReadyChanged;
        _multi.ClientGameStarted -= OnClientGameStarted;
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

    private void OnClientParticipantJoined(MultiplayerService.ParticipantInfo p)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!Participants.Any(x => x.Id == p.Id))
            {
                Participants.Add(new Participant { Id = p.Id, Name = p.Name, Image = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar, Ready = p.Ready });
                OnPropertyChanged(nameof(ParticipantsHeader));
            }
        });
    }

    private void OnClientParticipantLeft(string id)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Id == id);
            if (found is not null)
            {
                Participants.Remove(found);
                OnPropertyChanged(nameof(ParticipantsHeader));
            }
        });
    }

    private void OnClientParticipantReadyChanged(string id, bool ready)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Id == id);
            if (found is not null)
            {
                found.Ready = ready;
            }
        });
    }

    private void OnClientGameStarted()
    {
        // Navigate player into the game when host starts
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            // Avoid stacking multiple instances if event fires twice
            var top = Navigation.NavigationStack.LastOrDefault();
            if (top is not PlayerBuzzPage)
            {
                await Navigation.PushAsync(new PlayerBuzzPage());
            }
        });
    }

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        // Optional: do not disconnect to avoid re-JOIN; just navigate back
        await Navigation.PopAsync();
    }

    private async void OnReadyTapped(object? sender, EventArgs e)
    {
        LocalReady = !LocalReady;
        await _multi.SendReadyAsync(LocalReady);
    }

    public class Participant : INotifyPropertyChanged
    {
        private string _id = "";
        private string _name = "";
        private string _image = "avatar1.png";
        private bool _ready;

        public string Id { get => _id; set { if (_id == value) return; _id = value; OnPropertyChanged(); } }
        public string Name { get => _name; set { if (_name == value) return; _name = value; OnPropertyChanged(); } }
        public string Image { get => _image; set { if (_image == value) return; _image = value; OnPropertyChanged(); } }
        public bool Ready { get => _ready; set { if (_ready == value) return; _ready = value; OnPropertyChanged(); } }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
