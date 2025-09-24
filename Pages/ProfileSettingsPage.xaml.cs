using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using mindvault.Services;

namespace mindvault.Pages;

public partial class ProfileSettingsPage : ContentPage
{
    public ObservableCollection<string> Avatars { get; } = new()
    {
        "avatar1.png","avatar2.png","avatar3.png","avatar4.png","avatar5.png"
    };

    private string _selectedAvatar = string.Empty;
    public string SelectedAvatar
    {
        get => _selectedAvatar;
        set { var v = value ?? string.Empty; if (_selectedAvatar == v) return; _selectedAvatar = v; OnPropertyChanged(nameof(SelectedAvatar)); }
    }

    ProfileGender _selectedGender = ProfileGender.Unknown;

    public ProfileSettingsPage()
    {
        InitializeComponent();
        BindingContext = this;

        // Defaults / restore
        SelectedAvatar = string.IsNullOrWhiteSpace(ProfileState.Avatar) ? (Avatars.FirstOrDefault() ?? string.Empty) : ProfileState.Avatar;
        _selectedGender = ProfileState.Gender;
        UpdateGenderHighlights();
        UsernameEntry.Text = ProfileState.Name ?? string.Empty;
    }

    static bool IsValidUsername(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        return Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9]{3,14}$");
    }

    void SelectGender(ProfileGender gender) { _selectedGender = gender; UpdateGenderHighlights(); }
    void UpdateGenderHighlights()
    {
        double sel = 1.08, norm = 1.0;
        FemaleIcon.Scale = _selectedGender == ProfileGender.Female ? sel : norm;
        MaleIcon.Scale   = _selectedGender == ProfileGender.Male   ? sel : norm;
        OtherIcon.Scale  = _selectedGender == ProfileGender.Other  ? sel : norm;
        FemaleIcon.Opacity = _selectedGender == ProfileGender.Female ? 1.0 : 0.8;
        MaleIcon.Opacity   = _selectedGender == ProfileGender.Male   ? 1.0 : 0.8;
        OtherIcon.Opacity  = _selectedGender == ProfileGender.Other  ? 1.0 : 0.8;
    }

    void OnFemaleTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Female);
    void OnMaleTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Male);
    void OnOtherTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Other);

    private async void OnSaveClicked(object? sender, EventArgs e)
    {
        var name = UsernameEntry.Text?.Trim() ?? string.Empty;
        if (!IsValidUsername(name))
        {
            await DisplayAlert("Invalid Name", "Please enter a valid username.", "OK");
            return;
        }
        ProfileState.Name = name;
        ProfileState.Avatar = SelectedAvatar;
        ProfileState.Gender = _selectedGender;
        await DisplayAlert("Saved", "Profile updated.", "OK");
        await Navigation.PopAsync();
    }
}
