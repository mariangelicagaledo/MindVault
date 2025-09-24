using mindvault.Services;
using mindvault.Utils;
using System.Text.RegularExpressions;
using System.Threading;

namespace mindvault.Pages;

public partial class MultiplayerPage : ContentPage
{
    // Valid: exactly 5 alphanumeric (A–Z, 0–9)
    static readonly Regex CodeRx = new(@"^[A-Z0-9]{5}$", RegexOptions.Compiled);
    private readonly MultiplayerService _multi;

    private volatile bool _isJoining;
    private int _joinGate; // 0=free,1=busy

    public MultiplayerPage()
    {
        InitializeComponent();
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        _multi = ServiceHelper.GetRequiredService<MultiplayerService>();
    }

    private async void OnHostTapped(object? sender, TappedEventArgs e)
    {
        if (!_multi.HasLocalNetworkPath())
        {
            await DisplayAlert("Network", "Turn on Wi‑Fi hotspot or connect to a LAN/Wi‑Fi network, then try again.", "OK");
            return;
        }

        var code = _multi.GenerateRoomCode();
        var (ok, error) = await _multi.StartHostingAsync(code);
        if (!ok)
        {
            await DisplayAlert("Hosting", error ?? "Failed to start hosting.", "OK");
            return;
        }

        await PageHelpers.SafeNavigateAsync(this, async () => await Navigation.PushAsync(new HostLobbyPage(code)),
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
        var raw = e.NewTextValue ?? string.Empty;
        var cleaned = new string(raw.ToUpperInvariant().Where(ch => char.IsLetterOrDigit(ch)).ToArray());

        if (cleaned != raw)
        {
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
        var enabled = valid && !_isJoining;
        JoinBtn.Opacity = enabled ? 1.0 : 0.5;
        JoinBtn.InputTransparent = !enabled;
        JoinIcon.TextColor = enabled ? Colors.Black : Color.FromArgb("#93CFF9");
    }

    async Task JoinAsync()
    {
        // Interlocked gate prevents duplicate joins from tap + keyboard
        if (Interlocked.CompareExchange(ref _joinGate, 1, 0) != 0) return;
        try
        {
            if (_isJoining) return;
            var code = RoomCodeEntry?.Text ?? string.Empty;
            if (!CodeRx.IsMatch(code))
            {
                await DisplayAlert("Room Code", "Please enter a valid 5-character code (letters or numbers).", "OK");
                return;
            }

            _isJoining = true;
            UpdateJoinState();

            _multi.SetJoinedRoom(string.Empty);
            var (ok, error) = await _multi.DiscoverHostAsync(code, TimeSpan.FromSeconds(2));
            if (!ok)
            {
                await DisplayAlert("Join", error ?? "Room not found. Check the code and ensure both devices are on the same Wi‑Fi or hotspot.", "OK");
                return;
            }

            _multi.SetJoinedRoom(code);

            // Avoid pushing duplicate PlayerLobbyPage if already on top
            var top = Navigation.NavigationStack.LastOrDefault();
            if (top is not PlayerLobbyPage)
            {
                await PageHelpers.SafeNavigateAsync(this, async () => await Navigation.PushAsync(new PlayerLobbyPage()),
                    "Could not open player lobby");
            }
        }
        finally
        {
            _isJoining = false;
            UpdateJoinState();
            Interlocked.Exchange(ref _joinGate, 0);
        }
    }

    async void OnJoinTapped(object? sender, TappedEventArgs e) => await JoinAsync();

    void OnJoinCompleted(object? sender, EventArgs e)
    {
        _ = JoinAsync();
    }
}
