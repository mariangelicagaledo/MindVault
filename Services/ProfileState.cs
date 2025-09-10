using Microsoft.Maui.Storage;

namespace mindvault.Services;

public enum ProfileGender
{
    Unknown = 0,
    Female = 1,
    Male = 2,
    Other = 3
}

public static class ProfileState
{
    const string NameKey = "ProfileName";
    const string AvatarKey = "ProfileAvatar";
    const string GenderKey = "ProfileGender";

    public static string Name
    {
        get => Preferences.Get(NameKey, string.Empty);
        set => Preferences.Set(NameKey, value ?? string.Empty);
    }

    public static string Avatar
    {
        get => Preferences.Get(AvatarKey, string.Empty);
        set => Preferences.Set(AvatarKey, value ?? string.Empty);
    }

    public static ProfileGender Gender
    {
        get => (ProfileGender)Preferences.Get(GenderKey, (int)ProfileGender.Unknown);
        set => Preferences.Set(GenderKey, (int)value);
    }

    public static bool HasName => !string.IsNullOrWhiteSpace(Name);
}
