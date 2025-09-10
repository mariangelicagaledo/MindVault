using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using mindvault.Services;

namespace mindvault.Pages;

public partial class SetProfilePage : ContentPage
{
    // Put these files in Resources/Images/
    public ObservableCollection<string> Avatars { get; } = new()
    {
        "avatar1.png",
        "avatar2.png",
        "avatar3.png",
        "avatar4.png",
        "avatar5.png"
    };

    private string _selectedAvatar = string.Empty; // non-null
    public string SelectedAvatar
    {
        get => _selectedAvatar;
        set
        {
            var v = value ?? string.Empty;
            if (_selectedAvatar == v) return;
            _selectedAvatar = v;
            OnPropertyChanged(nameof(SelectedAvatar));
        }
    }

    public SetProfilePage()
    {
        InitializeComponent();
        BindingContext = this;

        // Default selection
        SelectedAvatar = Avatars.FirstOrDefault() ?? string.Empty;

        // Pre-fill if name exists
        var existing = ProfileState.Name;
        if (!string.IsNullOrWhiteSpace(existing))
            UsernameEntry.Text = existing;
    }

    static bool IsValidUsername(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        // 4-15 chars, start with a letter, letters/numbers only
        return Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9]{3,14}$");
    }

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var name = UsernameEntry.Text?.Trim() ?? string.Empty;
        if (!IsValidUsername(name))
        {
            await DisplayAlert("Invalid Name", "Please enter a valid username.", "OK");
            return;
        }

        ProfileState.Name = name; // persist

        // After successful save, go to Home
        await Shell.Current.GoToAsync("///HomePage");
    }
}
