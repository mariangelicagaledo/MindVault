namespace mindvault;

public partial class MainPage : ContentPage
{
    public MainPage()
    {
        InitializeComponent();
    }

    int _count;
    void OnCounterClicked(object sender, EventArgs e)
    {
        _count++;
        CounterBtn.Text = $"Clicked {_count}";
    }
}
