using CommunityToolkit.Mvvm.Messaging.Messages;

namespace mindvault.Utils.Messages;

public sealed class RoundSizeChangedMessage : ValueChangedMessage<(int ReviewerId, int RoundSize)>
{
    public RoundSizeChangedMessage(int reviewerId, int roundSize) : base((reviewerId, roundSize)) { }
}

public sealed class StudyModeChangedMessage : ValueChangedMessage<(int ReviewerId, string Mode)>
{
    public StudyModeChangedMessage(int reviewerId, string mode) : base((reviewerId, mode)) { }
}

public sealed class ProgressResetMessage : ValueChangedMessage<int>
{
    public ProgressResetMessage(int reviewerId) : base(reviewerId) { }
}
