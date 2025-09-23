using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mindvault.Services;
using mindvault.Data;

namespace mindvault.Pages;

public partial class HostLobbyPage : ContentPage, INotifyPropertyChanged
{
    public ObservableCollection<string> FlashcardOptions { get; } = new();

    private string _selectedFlashcard = string.Empty;
    public string SelectedFlashcard
    {
        get => _selectedFlashcard;
        set { if (_selectedFlashcard == value) return; _selectedFlashcard = value; OnPropertyChanged(); }
    }

    private string _roomCode = string.Empty;
    public string RoomCode
    {
        get => _roomCode;
        set { if (_roomCode == value) return; _roomCode = value; OnPropertyChanged(); }
    }

    public ObservableCollection<Participant> Participants { get; } = new();

    public string ParticipantsHeader => $"Participants: {Participants.Count}/8";

    private bool _canStart;
    public bool CanStart
    {
        get => _canStart;
        set { if (_canStart == value) return; _canStart = value; OnPropertyChanged(); }
    }

    private readonly MultiplayerService _multi = Services.ServiceHelper.GetRequiredService<MultiplayerService>();
    private readonly DatabaseService _db = Services.ServiceHelper.GetRequiredService<DatabaseService>();

    private List<Reviewer> _reviewers = new();

    public HostLobbyPage(string roomCode)
    {
        InitializeComponent();
        BindingContext = this;
        RoomCode = roomCode;

        // Subscribe to host-side events to reflect joins/leaves
        _multi.HostParticipantJoined += OnHostParticipantJoined;
        _multi.HostParticipantLeft += OnHostParticipantLeft;
        _multi.HostParticipantReadyChanged += OnHostParticipantReadyChanged;

        OnPropertyChanged(nameof(ParticipantsHeader));
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDeckOptionsAsync();

        // Hydrate lobby with current host snapshot (players currently in the lobby)
        RefreshParticipantsFromHostSnapshot();

        RecalculateCanStart();
    }

    private async Task LoadDeckOptionsAsync()
    {
        try
        {
            _reviewers = await _db.GetReviewersAsync();
            FlashcardOptions.Clear();
            foreach (var r in _reviewers)
                FlashcardOptions.Add(r.Title);

            if (string.IsNullOrEmpty(SelectedFlashcard) && FlashcardOptions.Count > 0)
                SelectedFlashcard = FlashcardOptions[0];
        }
        catch { }
    }

    private void RefreshParticipantsFromHostSnapshot()
    {
        var snapshot = _multi.GetHostParticipantsSnapshot();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            Participants.Clear();
            foreach (var p in snapshot)
            {
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

    private void OnHostParticipantJoined(MultiplayerService.ParticipantInfo p)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!Participants.Any(x => x.Id == p.Id))
            {
                Participants.Add(new Participant { Id = p.Id, Name = p.Name, Image = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar, Ready = p.Ready });
                OnPropertyChanged(nameof(ParticipantsHeader));
                RecalculateCanStart();
            }
        });
    }

    private void OnHostParticipantLeft(string id)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Id == id);
            if (found is not null)
            {
                Participants.Remove(found);
                OnPropertyChanged(nameof(ParticipantsHeader));
                RecalculateCanStart();
            }
        });
    }

    private void OnHostParticipantReadyChanged(string id, bool ready)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            var found = Participants.FirstOrDefault(x => x.Id == id);
            if (found is not null)
            {
                found.Ready = ready;
            }
            RecalculateCanStart();
        });
    }

    private void RecalculateCanStart()
    {
        CanStart = _multi.AreAllParticipantsReady();
    }

    // Model for the grid
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

    private async void OnBackTapped(object? sender, EventArgs e)
    {
        _multi.StopHosting();
        await Navigation.PopAsync();
    }

    private async void OnLetsGoTapped(object? sender, EventArgs e)
    {
        var (started, error) = await _multi.TryStartGameAsync();
        if (!started)
        {
            await DisplayAlert("Not Ready", error ?? "Not all participants are ready.", "OK");
            return;
        }

        var selected = _reviewers.FirstOrDefault(r => r.Title == SelectedFlashcard);
        if (selected is null)
        {
            await DisplayAlert("Start", "Please select a deck.", "OK");
            return;
        }

        await Navigation.PushAsync(new HostJudgePage(selected.Id, selected.Title));
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
