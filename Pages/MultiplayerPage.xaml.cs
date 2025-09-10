using mindvault.Utils;
using System.Text.RegularExpressions;

namespace mindvault.Pages;

public partial class MultiplayerPage : ContentPage
{
    // Valid: exactly 5 alphanumeric (A–Z, 0–9)
    static readonly Regex CodeRx = new(@"^[A-Z0-9]{5}$", RegexOptions.Compiled);

    public MultiplayerPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    private async void OnHostTapped(object? sender, TappedEventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await Navigation.PushAsync(new HostLobbyPage()),
            "Could not open host lobby");
    }

    private async void OnPlayerTapped(object? sender, EventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await Navigation.PushAsync(new PlayerLobbyPage()),
            "Could not open player lobby");
    }

    private async void OnCloseTapped(object? sender, EventArgs e)
        => await PageHelpers.SafeNavigateAsync(this, async () => await Navigation.PopAsync(),
            "Could not go back");

    void OnRoomCodeTextChanged(object? sender, TextChangedEventArgs e)
    {
        // Force uppercase and strip non-alphanumerics
        var raw = e.NewTextValue ?? string.Empty;
        var cleaned = new string(raw.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());

        if (cleaned != raw)
        {
            // set without re-entrancy issues
            RoomCodeEntry.TextChanged -= OnRoomCodeTextChanged;
            RoomCodeEntry.Text = cleaned;
            RoomCodeEntry.CursorPosition = cleaned.Length;
            RoomCodeEntry.TextChanged += OnRoomCodeTextChanged;
        }

        UpdateJoinState();
    }

    void UpdateJoinState()
    {
        var text = RoomCodeEntry?.Text ?? string.Empty;
        var valid = CodeRx.IsMatch(text);

        // enable/disable ✓
        JoinBtn.Opacity = valid ? 1.0 : 0.5;
        JoinBtn.InputTransparent = !valid;
        JoinIcon.TextColor = valid ? Colors.Black : Color.FromArgb("#93CFF9");
    }

    async void OnJoinTapped(object? sender, TappedEventArgs e)
    {
        var code = RoomCodeEntry?.Text ?? "";
        if (!CodeRx.IsMatch(code))
        {
            await DisplayAlert("Room Code", "Please enter a valid 5-character code (letters or numbers).", "OK");
            return;
        }

        // Demo: show confirmation (hook up real navigation later)
        await DisplayAlert("Join", $"Joining room: {code}", "OK");
    }

    // Fired when user presses the keyboard Done/Enter
    void OnJoinCompleted(object? sender, EventArgs e)
    {
        // Delegate to the same logic as tapping ✓
        OnJoinTapped(sender, new TappedEventArgs(null));
    }
}
