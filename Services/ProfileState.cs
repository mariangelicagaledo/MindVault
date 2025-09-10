using Microsoft.Maui.Storage;

namespace mindvault.Services;

public static class ProfileState
{
    const string NameKey = "ProfileName";

    public static string Name
    {
        get => Preferences.Get(NameKey, string.Empty);
        set => Preferences.Set(NameKey, value ?? string.Empty);
    }

    public static bool HasName => !string.IsNullOrWhiteSpace(Name);
}
