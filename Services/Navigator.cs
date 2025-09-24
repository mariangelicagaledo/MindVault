using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace mindvault.Services;

public static class Navigator
{
    static readonly SemaphoreSlim _gate = new(1, 1);
    static bool _isBusy;

    static async Task WithGate(Func<Task> action)
    {
        if (_isBusy) return;
        await _gate.WaitAsync();
        _isBusy = true;
        try { await action(); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Navigation error: {ex}"); }
        finally { _isBusy = false; _gate.Release(); }
    }

    public static Task GoToAsync(string route) => WithGate(async () =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            if (Shell.Current is not null)
                await Shell.Current.GoToAsync(route);
        }));

    public static Task PushAsync(Page page, INavigation nav) => WithGate(async () =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await nav.PushAsync(page);
        }));

    public static Task PopAsync(INavigation nav) => WithGate(async () =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await nav.PopAsync();
        }));

    public static Task PopToRootAsync(INavigation nav) => WithGate(async () =>
        await MainThread.InvokeOnMainThreadAsync(async () =>
        {
            await nav.PopToRootAsync();
        }));
}


