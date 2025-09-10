namespace mindvault.Pages;

public partial class HostJudgePage : ContentPage
{
    public HostJudgePage()
    {
        InitializeComponent();
    }

    private async void OnSkip(object? s, TappedEventArgs e)
        => await DisplayAlert("Skip", "Move to another card (demo).", "OK");

    private async void OnAccept(object? s, TappedEventArgs e)
        => await DisplayAlert("Accepted", "Point awarded (demo).", "OK");

    private async void OnReject(object? s, TappedEventArgs e)
        => await DisplayAlert("Rejected", "No point awarded (demo).", "OK");
}
