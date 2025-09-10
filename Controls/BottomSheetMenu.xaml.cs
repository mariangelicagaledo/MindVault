using System.Collections.ObjectModel;
using System.Windows.Input;

namespace mindvault.Controls;

public partial class BottomSheetMenu : ContentView
{
    public event EventHandler? CreateTapped;
    public event EventHandler? BrowseTapped;
    public event EventHandler? ImportTapped;
    public event EventHandler? ExportTapped;
    public event EventHandler? MultiplayerTapped; // NEW

    public ObservableCollection<string> Items { get; } = new();
    public static readonly BindableProperty ItemSelectedCommandProperty =
        BindableProperty.Create(nameof(ItemSelectedCommand), typeof(ICommand), typeof(BottomSheetMenu));
    public ICommand? ItemSelectedCommand { get => (ICommand?)GetValue(ItemSelectedCommandProperty); set => SetValue(ItemSelectedCommandProperty, value); }

    public BottomSheetMenu()
    {
        InitializeComponent();
    }

    public async Task ShowAsync()
    {
        IsVisible = true;
        Overlay.Opacity = 0;
        Sheet.TranslationY = 420;
        await Task.WhenAll(
            Overlay.FadeTo(1, 180, Easing.CubicOut),
            Sheet.TranslateTo(0, 0, 220, Easing.CubicOut)
        );
    }

    public async Task HideAsync()
    {
        await Task.WhenAll(
            Overlay.FadeTo(0, 160, Easing.CubicIn),
            Sheet.TranslateTo(0, 420, 220, Easing.CubicIn)
        );
        IsVisible = false;
    }

    public async Task Open()
    {
        await ShowAsync();
    }

    // Backdrop + close
    async void OnBackdropTapped(object? s, TappedEventArgs e) => await HideAsync();
    async void OnCloseTapped(object? s, TappedEventArgs e) => await HideAsync();

    // Item taps (raise to page, also auto-close)
    async void OnCreateTapped(object? s, TappedEventArgs e) { CreateTapped?.Invoke(this, EventArgs.Empty); ItemSelectedCommand?.Execute("Title Reviewer"); await HideAsync(); }
    async void OnBrowseTapped(object? s, TappedEventArgs e) { BrowseTapped?.Invoke(this, EventArgs.Empty); ItemSelectedCommand?.Execute("Browse Reviewers"); await HideAsync(); }
    async void OnMultiplayerTapped(object? s, TappedEventArgs e) { MultiplayerTapped?.Invoke(this, EventArgs.Empty); ItemSelectedCommand?.Execute("Multiplayer Mode"); await HideAsync(); } // NEW
    async void OnImportTapped(object? s, TappedEventArgs e) { ImportTapped?.Invoke(this, EventArgs.Empty); ItemSelectedCommand?.Execute("Import Page"); await HideAsync(); }
    async void OnExportTapped(object? s, TappedEventArgs e) { ExportTapped?.Invoke(this, EventArgs.Empty); ItemSelectedCommand?.Execute("Export Page"); await HideAsync(); }
} 