using CommunityToolkit.Mvvm.Messaging.Messages;

namespace mindvault.Utils.Messages;

public sealed class RoundSizeChangedMessage : ValueChangedMessage<int>
{
    public RoundSizeChangedMessage(int value) : base(value) { }
}

public sealed class StudyModeChangedMessage : ValueChangedMessage<string>
{
    public StudyModeChangedMessage(string value) : base(value) { }
}

public sealed class ProgressResetMessage : ValueChangedMessage<int>
{
    public ProgressResetMessage(int reviewerId) : base(reviewerId) { }
}
