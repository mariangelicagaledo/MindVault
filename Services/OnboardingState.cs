using Microsoft.Maui.Storage;

namespace mindvault.Services;

public static class OnboardingState
{
    const string CompletedKey = "OnboardingCompleted";

    public static bool IsCompleted
    {
        get => Preferences.Get(CompletedKey, false);
        set => Preferences.Set(CompletedKey, value);
    }
}
