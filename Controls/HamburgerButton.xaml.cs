using System.Windows.Input;

namespace mindvault.Controls;

public partial class HamburgerButton : ContentView
{
    public event EventHandler? Clicked;

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(HamburgerButton));
    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(HamburgerButton));

    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }

    public HamburgerButton()
    {
        InitializeComponent();
    }

    async void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;
        IsEnabled = false;
        // tiny press animation
        await Root.ScaleTo(0.94, 70, Easing.CubicOut);
        await Root.ScaleTo(1.0, 90, Easing.CubicIn);
        Clicked?.Invoke(this, EventArgs.Empty);
        Command?.Execute(CommandParameter);
        IsEnabled = true;
    }
} 