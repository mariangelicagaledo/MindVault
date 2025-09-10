using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mindvault.Pages;

public partial class PlayerLobbyPage : ContentPage, INotifyPropertyChanged
{
    public ObservableCollection<Participant> Participants { get; } = new();

    public string ParticipantsHeader => $"Participants: {Participants.Count}/8";

    public PlayerLobbyPage()
    {
        InitializeComponent();
        BindingContext = this;

        // Static sample data – use avatars that already exist in Resources/Images
        var names = new[] { "kalabaw","kalabaw","kalabaw","kalabaw","kalabaw","kalabaw","kalabaw","kalabaw" };
        var images = new[] { "avatar1.png","avatar2.png","avatar3.png","avatar4.png","avatar5.png" };

        for (int i = 0; i < names.Length; i++)
            Participants.Add(new Participant { Name = names[i], Image = images[i % images.Length] });

        OnPropertyChanged(nameof(ParticipantsHeader));
    }

    private async void OnBackTapped(object? sender, EventArgs e)
        => await Navigation.PopAsync();

    private async void OnReadyTapped(object? sender, TappedEventArgs e)
    {
        await Navigation.PushAsync(new PlayerBuzzPage());
    }

    // model
    public class Participant
    {
        public string Name { get; set; } = "";
        public string Image { get; set; } = "avatar1.png";
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
