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

    ProfileGender _selectedGender = ProfileGender.Unknown;

    public SetProfilePage()
    {
        InitializeComponent();
        BindingContext = this;

        // Default selection
        SelectedAvatar = Avatars.FirstOrDefault() ?? string.Empty;

        // Restore saved state
        var existing = ProfileState.Name;
        if (!string.IsNullOrWhiteSpace(existing))
            UsernameEntry.Text = existing;

        var savedAvatar = ProfileState.Avatar;
        if (!string.IsNullOrWhiteSpace(savedAvatar) && Avatars.Contains(savedAvatar))
            SelectedAvatar = savedAvatar;

        _selectedGender = ProfileState.Gender;
        UpdateGenderHighlights();
    }

    static bool IsValidUsername(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        // 4-15 chars, start with a letter, letters/numbers only
        return Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9]{3,14}$");
    }

    void SelectGender(ProfileGender gender)
    {
        _selectedGender = gender;
        UpdateGenderHighlights();
    }

    void UpdateGenderHighlights()
    {
        // Slight scale/color emphasis for selected icon
        double selScale = 1.08;
        double normScale = 1.0;

        FemaleIcon.Scale = _selectedGender == ProfileGender.Female ? selScale : normScale;
        MaleIcon.Scale   = _selectedGender == ProfileGender.Male   ? selScale : normScale;
        OtherIcon.Scale  = _selectedGender == ProfileGender.Other  ? selScale : normScale;

        FemaleIcon.Opacity = _selectedGender == ProfileGender.Female ? 1.0 : 0.8;
        MaleIcon.Opacity   = _selectedGender == ProfileGender.Male   ? 1.0 : 0.8;
        OtherIcon.Opacity  = _selectedGender == ProfileGender.Other  ? 1.0 : 0.8;
    }

    void OnFemaleTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Female);
    void OnMaleTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Male);
    void OnOtherTapped(object? sender, TappedEventArgs e) => SelectGender(ProfileGender.Other);

    private async void OnSaveClicked(object sender, EventArgs e)
    {
        var name = UsernameEntry.Text?.Trim() ?? string.Empty;
        if (!IsValidUsername(name))
        {
            await DisplayAlert("Invalid Name", "Please enter a valid username.", "OK");
            return;
        }

        // Persist selections globally
        ProfileState.Name = name;
        ProfileState.Avatar = SelectedAvatar;
        ProfileState.Gender = _selectedGender;

        // After successful save, go to Home
        await Navigator.GoToAsync("///HomePage");
    }
}
