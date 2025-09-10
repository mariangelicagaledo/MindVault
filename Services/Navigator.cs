using System.Threading;
using System.Threading.Tasks;
using Microsoft.Maui.ApplicationModel;

namespace mindvault.Services;

public static class Navigator
{
    static readonly SemaphoreSlim _gate = new(1, 1);
    static bool _isBusy;

    public static async Task GoToAsync(string route)
    {
        if (_isBusy) return;
        await _gate.WaitAsync();
        _isBusy = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                if (Shell.Current is not null)
                    await Shell.Current.GoToAsync(route);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex}");
        }
        finally
        {
            _isBusy = false;
            _gate.Release();
        }
    }

    public static async Task PushAsync(Page page, INavigation nav)
    {
        if (_isBusy) return;
        await _gate.WaitAsync();
        _isBusy = true;
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                await nav.PushAsync(page);
            });
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Navigation error: {ex}");
        }
        finally
        {
            _isBusy = false;
            _gate.Release();
        }
    }
}


