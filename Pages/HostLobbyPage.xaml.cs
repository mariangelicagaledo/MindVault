using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mindvault.Pages;

public partial class HostLobbyPage : ContentPage, INotifyPropertyChanged
{
    public ObservableCollection<string> FlashcardOptions { get; } = new()
    {
        "All",
        "Last Played"
    };

    private string _selectedFlashcard = "Last Played";
    public string SelectedFlashcard
    {
        get => _selectedFlashcard;
        set { if (_selectedFlashcard == value) return; _selectedFlashcard = value; OnPropertyChanged(); }
    }

    public string RoomCode { get; } = "USHXJ";

    public ObservableCollection<Participant> Participants { get; } = new();

    public string ParticipantsHeader => $"Participants: {Participants.Count}/8";

    public HostLobbyPage()
    {
        InitializeComponent();
        BindingContext = this;

        // --- Static sample data using existing avatars in Resources/Images ---
        // Replace/extend with whatever avatar files you already have (avatar1..avatarX.png).
        var names = new[] { "kalabaw", "kalabaw", "kalabaw", "kalabaw", "kalabaw", "kalabaw", "kalabaw", "kalabaw" };
        var images = new[]
        {
            "avatar1.png","avatar2.png","avatar3.png","avatar4.png",
            "avatar5.png","avatar1.png","avatar3.png","avatar2.png"
        };

        for (int i = 0; i < names.Length; i++)
            Participants.Add(new Participant { Name = names[i], Image = images[i % images.Length] });

        OnPropertyChanged(nameof(ParticipantsHeader));
    }

    // Model for the grid
    public class Participant
    {
        public string Name { get; set; } = "";
        public string Image { get; set; } = "avatar1.png";
    }

    private async void OnBackTapped(object? sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnLetsGoTapped(object? sender, EventArgs e)
    {
        await Navigation.PushAsync(new HostJudgePage());
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
