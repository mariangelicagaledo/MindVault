using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mindvault.Services;
using mindvault.Utils;
using System.Diagnostics;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Media;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using mindvault.Utils.Messages;
using System.Threading;
using Microsoft.Maui.ApplicationModel;

namespace mindvault.Pages;

public partial class CourseReviewPage : ContentPage, INotifyPropertyChanged
{
    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();

    public new string Title { get; }
    public int ReviewerId { get; private set; }

    readonly List<SrsCard> _cards = new();

    SrsCard? _current;
    SrsCard? _lastAnswered;
    bool _front = true;
    bool _loaded;

    int _total;

    const string PrefRoundSize = "RoundSize";
    const string PrefStudyMode = "StudyMode"; // "Default" or "Exam"
    const string PrefReviewStatePrefix = "ReviewState_"; // + reviewerId
    const int MemorizedThreshold = 21;
    const double DefaultMaxMemorizedDays = 120.0;
    int _roundSize;
    int _roundCount;
    string _studyMode = "Default";

    readonly HashSet<SrsCard> _learnedEver = new();
    int _skilledCount = 0;
    int _memorizedCount = 0;

    // Session stats
    DateTime _sessionStart;
    public int CorrectCount { get; private set; }
    public int WrongCount { get; private set; }
    public string ElapsedText => $"Time: {(int)(DateTime.UtcNow - _sessionStart).TotalMinutes} min";

    bool _sessionComplete;
    public bool SessionComplete
    {
        get => _sessionComplete;
        set { if (_sessionComplete == value) return; _sessionComplete = value; OnPropertyChanged(); }
    }

    public int Avail => _total;
    public int Seen => _cards.Count(c => c.SeenOnce);
    public int Learned => _learnedEver.Count;
    public int Skilled => _skilledCount;
    public int Memorized => _memorizedCount;

    public string FaceTag => _front ? "[Front]" : "[Back]";
    public string FaceText => _current is null ? string.Empty : (_front ? _current.Question : _current.Answer);
    public string? FaceImage => _current is null ? null : (_front ? _current.QuestionImagePath : _current.AnswerImagePath);
    public bool FaceImageVisible => !string.IsNullOrWhiteSpace(FaceImage);

    double _progressWidth;
    public double ProgressWidth { get => _progressWidth; set { _progressWidth = value; OnPropertyChanged(); } }

    // scheduling parameters
    TimeSpan LearnStep1 = TimeSpan.FromMinutes(1);
    TimeSpan LearnStep2 = TimeSpan.FromMinutes(10);
    TimeSpan RelearnStep = TimeSpan.FromMinutes(10);
    int GraduateDays = 1;
    double StartEase = 2.5;
    double MinEase = 1.3;
    double AgainEasePenalty = 0.2;
    double NewIntervalPctAfterLapse = 0.0;

    static readonly int[] CramSeconds = new[] { 40, 52, 68, 88, 114, 149, 193, 251, 326, 424, 551, 717, 932, 1212, 1575, 2047, 2662, 3460, 4498, 5848 };
    List<TimeSpan>? _cramSchedule;

    public CourseReviewPage(int reviewerId, string title)
    {
        InitializeComponent();
        ReviewerId = reviewerId;
        Title = title;
        // per-deck settings defaults
        _roundSize = Preferences.Get($"{PrefRoundSize}_{ReviewerId}", Preferences.Get(PrefRoundSize, 10));
        _studyMode = Preferences.Get($"{PrefStudyMode}_{ReviewerId}", Preferences.Get(PrefStudyMode, "Default"));
        ApplyStudyMode(_studyMode);
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    public CourseReviewPage(string title = "Math Reviewer")
    {
        InitializeComponent();
        Title = title;
        // reviewer id will be resolved OnAppearing; use global defaults until then
        _roundSize = Preferences.Get(PrefRoundSize, 10);
        _studyMode = Preferences.Get(PrefStudyMode, "Default");
        ApplyStudyMode(_studyMode);
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
    }

    void WireMessages()
    {
        WeakReferenceMessenger.Default.Register<RoundSizeChangedMessage>(this, (r, m) =>
        {
            if (m.Value.ReviewerId != ReviewerId) return;
            if (_roundSize == m.Value.RoundSize) return; // no change
            _roundSize = m.Value.RoundSize;
            Preferences.Set($"{PrefRoundSize}_{ReviewerId}", _roundSize);
            _roundCount = 0; UpdateProgressWidth(); SaveProgress();
            _ = MainThread.InvokeOnMainThreadAsync(ResetSessionAsync);
        });
        WeakReferenceMessenger.Default.Register<StudyModeChangedMessage>(this, (r, m) =>
        {
            if (m.Value.ReviewerId != ReviewerId) return;
            if (_studyMode == m.Value.Mode) return; // no change
            _studyMode = m.Value.Mode;
            Preferences.Set($"{PrefStudyMode}_{ReviewerId}", _studyMode);
            ApplyStudyMode(_studyMode); SaveProgress();
            _ = MainThread.InvokeOnMainThreadAsync(ResetSessionAsync);
        });
        WeakReferenceMessenger.Default.Register<ProgressResetMessage>(this, (r, m) =>
        {
            if (m.Value != ReviewerId) return;
            _ = MainThread.InvokeOnMainThreadAsync(async () => await ResetSessionAsync());
        });
    }

    async Task ResetSessionAsync()
    {
        try
        {
            _roundJustFinished = false;
            SessionComplete = false;
            try { await CompletionOverlay.FadeTo(0, 120); } catch { }
            _loaded = true; // allow reload within the same lifetime
            await LoadDeckAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseReviewPage] ResetSessionAsync error: {ex}");
        }
    }

    void ApplyStudyMode(string mode)
    {
        if (mode == "Exam")
        {
            _cramSchedule = new List<TimeSpan>(CramSeconds.Length);
            foreach (var s in CramSeconds) _cramSchedule.Add(TimeSpan.FromSeconds(s));
            LearnStep1 = TimeSpan.FromMinutes(1);
            LearnStep2 = TimeSpan.FromMinutes(10);
            RelearnStep = TimeSpan.FromMinutes(10);
            GraduateDays = 1;
            StartEase = 2.5; MinEase = 1.3; AgainEasePenalty = 0.2; NewIntervalPctAfterLapse = 0.0;
        }
        else
        {
            _cramSchedule = null;
            LearnStep1 = TimeSpan.FromMinutes(1);
            LearnStep2 = TimeSpan.FromMinutes(10);
            RelearnStep = TimeSpan.FromMinutes(10);
            GraduateDays = 1;
            StartEase = 2.5;
            MinEase = 1.3;
            AgainEasePenalty = 0.2;
            NewIntervalPctAfterLapse = 0.0;
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Resolve reviewer id if needed
        if (ReviewerId <= 0)
        {
            var reviewers = await _db.GetReviewersAsync();
            var match = reviewers.FirstOrDefault(r => r.Title == Title);
            if (match is not null)
                ReviewerId = match.Id;
        }

        // rewire messages every appear to ensure subscriptions exist after returning from settings
        WeakReferenceMessenger.Default.UnregisterAll(this);
        WireMessages();

        // Detect deck-scoped settings changes while we were away
        var prevRound = _roundSize;
        var prevMode = _studyMode;

        var newRound = Preferences.Get($"{PrefRoundSize}_{ReviewerId}", Preferences.Get(PrefRoundSize, prevRound));
        var newMode = Preferences.Get($"{PrefStudyMode}_{ReviewerId}", Preferences.Get(PrefStudyMode, prevMode));

        bool changed = (newRound != prevRound) || !string.Equals(newMode, prevMode, StringComparison.Ordinal);

        // Apply values
        _roundSize = newRound;
        if (!string.Equals(newMode, _studyMode, StringComparison.Ordinal))
        {
            _studyMode = newMode;
            ApplyStudyMode(_studyMode);
        }

        if (changed)
        {
            await ResetSessionAsync();
            return;
        }

        if (_loaded) return;
        _sessionStart = DateTime.UtcNow;
        await LoadDeckAsync();
        _loaded = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveProgress();
        // Do not unregister from messages here so we can react to reset while settings page is on top
    }

    async Task LoadDeckAsync()
    {
        _cards.Clear();
        _learnedEver.Clear();
        _skilledCount = 0;
        _memorizedCount = 0;
        CorrectCount = 0; WrongCount = 0;
        _roundJustFinished = false;
        if (ReviewerId > 0)
        {
            var cards = await _db.GetFlashcardsAsync(ReviewerId);
            foreach (var c in cards)
                _cards.Add(new SrsCard(c.Id, c.Question, c.Answer, c.QuestionImagePath, c.AnswerImagePath) { Ease = StartEase });
        }
        _total = _cards.Count;

        RestoreProgress();

        _current = null;
        _lastAnswered = null;
        _roundCount = 0;
        _front = true;
        SessionComplete = false;
        UpdateBindingsAll();
        PickNextCard();
    }

    async Task FlipAnimationAsync()
    {
        try
        {
            await CardBorder.ScaleXTo(0.0, 110, Easing.CubicIn);
            await CardBorder.ScaleXTo(1.0, 110, Easing.CubicOut);
        }
        catch { }
    }

    private async void OnFlip(object? s, TappedEventArgs e)
    {
        await FlipAnimationAsync();
        _front = !_front;
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(FaceImage));
        OnPropertyChanged(nameof(FaceImageVisible));
    }

    private void OnSkip(object? s, TappedEventArgs e)
    {
        _lastAnswered = _current;
        PickNextCard();
    }

    private void OnFail(object? s, TappedEventArgs e)
    {
        if (_current is null) { PickNextCard(); return; }
        WrongCount++; OnPropertyChanged(nameof(WrongCount));
        var now = DateTime.UtcNow;

        if (_current.CountedMemorized)
        {
            _memorizedCount = Math.Max(0, _memorizedCount - 1);
            _current.CountedMemorized = false;
            OnPropertyChanged(nameof(Memorized));
        }
        if (_current.CountedSkilled)
        {
            _skilledCount = Math.Max(0, _skilledCount - 1);
            _current.CountedSkilled = false;
            OnPropertyChanged(nameof(Skilled));
        }

        _current.Stage = Stage.Learned;
        _current.InReview = true;
        _current.ReviewSuccessStreak = 0;

        if (_cramSchedule is not null)
        {
            _current.CramIndex = 0;
            _current.Interval = _cramSchedule[0];
            _current.DueAt = now + _current.Interval;
        }
        else
        {
            _current.Interval = RelearnStep;
            _current.DueAt = now + _current.Interval;
        }

        SaveProgress();
        UpdateAnswerWindow(now);
        _front = true;
        IncrementRound();
        UpdateBindingsAll();
        PickNextCard();
    }

    private void OnPass(object? s, TappedEventArgs e)
    {
        if (_current is null) { PickNextCard(); return; }
        CorrectCount++; OnPropertyChanged(nameof(CorrectCount));
        var now = DateTime.UtcNow;

        if (!_current.InReview)
        {
            if (_current.IsRelearning)
            {
                _current.InReview = true;
                _current.IsRelearning = false;
                _current.LearningIndex = -1;
                if (_cramSchedule is not null)
                {
                    _current.CramIndex = 0;
                    _current.Interval = _cramSchedule[0];
                    _current.DueAt = now + _current.Interval;
                }
                else
                {
                    double stepDays = DefaultMaxMemorizedDays / MemorizedThreshold;
                    _current.Interval = TimeSpan.FromDays(stepDays);
                    _current.DueAt = now + _current.Interval;
                }
                _current.Stage = Stage.Learned;
                _learnedEver.Add(_current);
                OnPropertyChanged(nameof(Learned));
            }
            else
            {
                if (_current.SeenCount >= 2)
                {
                    _current.InReview = true;
                    _current.IsRelearning = false;
                    _current.LearningIndex = -1;
                    if (_cramSchedule is not null)
                    {
                        _current.CramIndex = 0;
                        _current.Interval = _cramSchedule[0];
                        _current.DueAt = now + _current.Interval;
                    }
                    else
                    {
                        double stepDays = DefaultMaxMemorizedDays / MemorizedThreshold;
                        _current.Interval = TimeSpan.FromDays(stepDays);
                        _current.DueAt = now + _current.Interval;
                    }
                    _current.Stage = Stage.Learned;
                    _learnedEver.Add(_current);
                    OnPropertyChanged(nameof(Learned));
                }
                else
                {
                    _current.Stage = Stage.Seen;
                    _current.IsRelearning = false;
                    _current.LearningIndex = 0;
                    _current.DueAt = now + LearnStep1;
                    _current.Interval = LearnStep1;
                }
            }
        }
        else
        {
            if (_cramSchedule is not null)
            {
                var idx = Math.Clamp(_current.CramIndex, 0, _cramSchedule.Count - 1);
                _current.Interval = _cramSchedule[idx];
                _current.DueAt = now + _current.Interval;
                if (_current.CramIndex < _cramSchedule.Count - 1) _current.CramIndex++;
            }
            else
            {
                int nextStreak = _current.ReviewSuccessStreak + 1;
                double stepDays = DefaultMaxMemorizedDays / MemorizedThreshold;
                double nextDays = Math.Min(DefaultMaxMemorizedDays, stepDays * nextStreak);
                _current.Interval = TimeSpan.FromDays(nextDays);
                _current.DueAt = now + _current.Interval;
            }

            _learnedEver.Add(_current);
            OnPropertyChanged(nameof(Learned));
            _current.ReviewSuccessStreak++;
            if (_current.ReviewSuccessStreak == 1 && !_current.CountedSkilled)
            {
                _skilledCount++;
                _current.CountedSkilled = true;
                OnPropertyChanged(nameof(Skilled));
            }
            if (_current.ReviewSuccessStreak == MemorizedThreshold && !_current.CountedMemorized)
            {
                _memorizedCount++;
                _current.CountedMemorized = true;
                OnPropertyChanged(nameof(Memorized));
            }
        }

        SaveProgress();
        UpdateAnswerWindow(now);
        _front = true;
        IncrementRound();
        UpdateBindingsAll();
        PickNextCard();
    }

    private void OnBucketTapped(object? sender, TappedEventArgs e)
    {
        if (e.Parameter is not string bucket) return;
        var now = DateTime.UtcNow;
        SrsCard? pick = null;
        switch (bucket)
        {
            case "Avail":
                pick = _cards.FirstOrDefault(c => c.Stage == Stage.Avail);
                if (pick is not null)
                {
                    pick.Stage = Stage.Seen;
                    pick.DueAt = now;
                    pick.Interval = TimeSpan.Zero;
                    pick.LearningIndex = -1;
                }
                break;
            case "Seen":
                pick = _cards.Where(c => c.Stage == Stage.Seen && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault(c => !ReferenceEquals(c, _lastAnswered))
                    ?? _cards.Where(c => c.Stage == Stage.Seen && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault();
                break;
            case "Learned":
                pick = _cards.Where(c => c.InReview && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault(c => !ReferenceEquals(c, _lastAnswered))
                    ?? _cards.Where(c => c.InReview && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault();
                break;
            case "Skilled":
            case "Memorized":
                pick = _cards.Where(c => c.InReview && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault(c => !ReferenceEquals(c, _lastAnswered))
                    ?? _cards.Where(c => c.InReview && c.CooldownUntil <= now)
                             .OrderBy(c => c.DueAt)
                             .FirstOrDefault();
                break;
        }
        if (pick is not null)
        {
            _current = pick;
            AfterPick();
        }
    }

    // Re-introduced: limits rapid answer interactions and adds short cooldown
    void UpdateAnswerWindow(DateTime now)
    {
        if (_current is null) return;
        _current.AnswerTimes.Enqueue(now);
        var cutoff = now - TimeSpan.FromMinutes(1);
        while (_current.AnswerTimes.Count > 0 && _current.AnswerTimes.Peek() < cutoff)
            _current.AnswerTimes.Dequeue();

        _lastAnswered = _current;

        if (_current.AnswerTimes.Count >= 3)
        {
            _current.CooldownUntil = now + TimeSpan.FromSeconds(5);
            _current.AnswerTimes.Clear();
        }
    }

    // New: round-completion guard
    private bool _roundJustFinished;

    // Speak current card title/text
    CancellationTokenSource? _ttsCts;
    bool _isSpeaking;

    private async void OnSpeakTapped(object? s, TappedEventArgs e)
    {
        try
        {
            // Toggle cancel if already speaking
            if (_isSpeaking)
            {
                _ttsCts?.Cancel();
                return;
            }

            var text = FaceText;
            if (string.IsNullOrWhiteSpace(text)) return;

            _ttsCts = new CancellationTokenSource();
            _isSpeaking = true;
            SpeakButton.Opacity = 0.5; // visual feedback but keep it tappable to allow cancel

            await TextToSpeech.Default.SpeakAsync(text, new SpeechOptions { Volume = 1.0f, Pitch = 1.0f }, _ttsCts.Token);
        }
        catch (OperationCanceledException)
        {
            // expected on cancel
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[TTS] {ex.Message}");
        }
        finally
        {
            _isSpeaking = false;
            _ttsCts?.Dispose();
            _ttsCts = null;
            SpeakButton.Opacity = 1.0;
        }
    }

    // Adapt PickNextCard to only show completion UI when round-size done
    void PickNextCard()
    {
        var now = DateTime.UtcNow;

        var dueList = _cards.Where(c => c.Stage != Stage.Avail && c.DueAt <= now && c.CooldownUntil <= now)
                            .OrderBy(c => c.DueAt)
                            .ToList();
        var dueNow = dueList.FirstOrDefault(c => !ReferenceEquals(c, _lastAnswered))
                    ?? dueList.FirstOrDefault();
        if (dueNow is not null)
        {
            _current = dueNow;
            AfterPick();
            return;
        }

        var avail = _cards.FirstOrDefault(c => c.Stage == Stage.Avail);
        if (avail is not null)
        {
            avail.Stage = Stage.Seen;
            avail.LearningIndex = -1;
            avail.DueAt = now;
            avail.Interval = TimeSpan.Zero;
            _current = avail;
            AfterPick();
            return;
        }

        var futureList = _cards.Where(c => c.Stage != Stage.Avail && c.CooldownUntil <= now)
                               .OrderBy(c => c.DueAt)
                               .ToList();
        var nextFuture = futureList.FirstOrDefault(c => !ReferenceEquals(c, _lastAnswered))
                      ?? futureList.FirstOrDefault();
        _current = nextFuture;

        // When no next card: only show completion overlay if a full round was completed
        if (_current is null)
        {
            if (_roundJustFinished || _roundSize == 0)
            {
                SessionComplete = true;
                _ = ShowCompletionAsync();
            }
        }
        else
        {
            AfterPick();
        }
    }

    void IncrementRound()
    {
        _roundCount++;
        UpdateProgressWidth();
        if (_roundSize > 0 && _roundCount >= _roundSize)
        {
            _roundJustFinished = true;
            _roundCount = 0;
            _lastAnswered = null;
            SaveProgress();
            SessionComplete = true;
            _ = ShowCompletionAsync();
        }
    }

    // === Helpers restored ===
    async Task ShowCompletionAsync()
    {
        try
        {
            await CompletionOverlay.FadeTo(1, 180, Easing.CubicOut);
            // Simple confetti: spawn fading dots
            _ = Task.Run(async () =>
            {
                for (int i = 0; i < 12; i++)
                {
                    await MainThread.InvokeOnMainThreadAsync(async () =>
                    {
                        var dot = new BoxView { Color = Color.FromRgb(Random.Shared.Next(256), Random.Shared.Next(256), Random.Shared.Next(256)), CornerRadius = 6, WidthRequest = 12, HeightRequest = 12, Opacity = 0 };
                        ConfettiHost.Children.Add(dot);
                        await dot.FadeTo(1, 100);
                        await dot.TranslateTo(0, 240, (uint)(700 + Random.Shared.Next(300)), Easing.CubicIn);
                        await dot.FadeTo(0, 150);
                        ConfettiHost.Children.Remove(dot);
                    });
                    await Task.Delay(90);
                }
            });
        }
        catch { }
    }

    void AfterPick()
    {
        _front = true;
        if (_current is not null)
        {
            _current.SeenOnce = true;
            _current.SeenCount++;
        }
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(FaceImage));
        OnPropertyChanged(nameof(FaceImageVisible));
        UpdateBindingsAll();
    }

    void UpdateBindingsAll()
    {
        OnPropertyChanged(nameof(Avail));
        OnPropertyChanged(nameof(Seen));
        OnPropertyChanged(nameof(Learned));
        OnPropertyChanged(nameof(Skilled));
        OnPropertyChanged(nameof(Memorized));
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
        OnPropertyChanged(nameof(FaceImage));
        OnPropertyChanged(nameof(FaceImageVisible));
        OnPropertyChanged(nameof(ElapsedText));
        OnPropertyChanged(nameof(CorrectCount));
        OnPropertyChanged(nameof(WrongCount));
        UpdateProgressWidth();
    }

    void UpdateProgressWidth()
    {
        double ratio = _roundSize == 0 ? 0 : Math.Clamp((double)_roundCount / _roundSize, 0, 1);
        ProgressWidth = (Width - 32) * ratio;
    }

    // ---- persistence ----
    record CardStateDto(
        int Id,
        string Question,
        string Answer,
        string Stage,
        long DueAtTicks,
        double IntervalDays,
        int Lapses,
        double Ease,
        int LearningIndex,
        bool IsRelearning,
        bool InReview,
        double LastIntervalDays,
        int ReviewSuccessStreak,
        bool SeenOnce,
        int SeenCount,
        long CooldownUntilTicks,
        bool CountedSkilled,
        bool CountedMemorized,
        int CramIndex
    );

    void SaveProgress()
    {
        try
        {
            var list = _cards.Select(c => new CardStateDto(
                c.Id,
                c.Question,
                c.Answer,
                c.Stage.ToString(),
                c.DueAt.Ticks,
                c.Interval.TotalDays,
                c.Lapses,
                c.Ease,
                c.LearningIndex,
                c.IsRelearning,
                c.InReview,
                c.LastIntervalDays,
                c.ReviewSuccessStreak,
                c.SeenOnce,
                c.SeenCount,
                c.CooldownUntil.Ticks,
                c.CountedSkilled,
                c.CountedMemorized,
                c.CramIndex
            )).ToList();
            var payload = JsonSerializer.Serialize(list);
            Preferences.Set(PrefReviewStatePrefix + ReviewerId, payload);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseReviewPage] SaveProgress error: {ex}");
        }
    }

    void RestoreProgress()
    {
        try
        {
            var payload = Preferences.Get(PrefReviewStatePrefix + ReviewerId, null);
            if (string.IsNullOrWhiteSpace(payload)) return;
            var list = JsonSerializer.Deserialize<List<CardStateDto>>(payload);
            if (list is null) return;
            var map = _cards.ToDictionary(c => c.Id);
            foreach (var dto in list)
            {
                if (!map.TryGetValue(dto.Id, out var c)) continue;
                if (Enum.TryParse<Stage>(dto.Stage, out var st)) c.Stage = st;
                c.DueAt = new DateTime(dto.DueAtTicks, DateTimeKind.Utc);
                c.Interval = TimeSpan.FromDays(dto.IntervalDays);
                c.Lapses = dto.Lapses;
                c.Ease = dto.Ease;
                c.LearningIndex = dto.LearningIndex;
                c.IsRelearning = dto.IsRelearning;
                c.InReview = dto.InReview;
                c.LastIntervalDays = dto.LastIntervalDays;
                c.ReviewSuccessStreak = dto.ReviewSuccessStreak;
                c.SeenOnce = dto.SeenOnce;
                c.SeenCount = dto.SeenCount;
                c.CooldownUntil = new DateTime(dto.CooldownUntilTicks, DateTimeKind.Utc);
                c.CountedSkilled = dto.CountedSkilled;
                c.CountedMemorized = dto.CountedMemorized;
                c.CramIndex = dto.CramIndex;
                if (c.CountedSkilled) _skilledCount++;
                if (c.CountedMemorized) _memorizedCount++;
                if (c.InReview) _learnedEver.Add(c);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CourseReviewPage] RestoreProgress error: {ex}");
        }
    }

    enum Stage { Avail, Seen, Learned, Skilled, Memorized }
    class SrsCard
    {
        public int Id { get; }
        public string Question { get; }
        public string Answer { get; }
        public string QuestionImagePath { get; }
        public string AnswerImagePath { get; }
        public Stage Stage { get; set; } = Stage.Avail;
        public DateTime DueAt { get; set; } = DateTime.MinValue;
        public TimeSpan Interval { get; set; } = TimeSpan.Zero;
        public int Lapses { get; set; } = 0;
        public double Ease { get; set; } = 2.5; // default; overridden on load by current mode
        public int LearningIndex { get; set; } = -1;
        public bool IsRelearning { get; set; } = false;
        public bool InReview { get; set; } = false;
        public double LastIntervalDays { get; set; } = 0;
        public int ReviewSuccessStreak { get; set; } = 0;
        public bool SeenOnce { get; set; } = false;
        public int SeenCount { get; set; } = 0;
        public DateTime CooldownUntil { get; set; } = DateTime.MinValue;
        public Queue<DateTime> AnswerTimes { get; } = new();
        public bool CountedSkilled { get; set; } = false;
        public bool CountedMemorized { get; set; } = false;
        public int CramIndex { get; set; } = 0;
        public SrsCard(int id, string q, string a, string qImg, string aImg) { Id = id; Question = q; Answer = a; QuestionImagePath = qImg; AnswerImagePath = aImg; }
    }

    public class QueueItem
    {
        public string QuestionPreview { get; set; } = string.Empty;
        public string DueInText { get; set; } = string.Empty;
    }

    public new event PropertyChangedEventHandler? PropertyChanged;
    protected new void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private async void OnCloseTapped(object? s, EventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseCourseToReviewers(),
            "Could not return to reviewers");
    }

    private async void OnSettingsTapped(object? s, EventArgs e)
    {
        // Push settings on the same Navigation stack to keep deck context and enable reliable close
        var id = ReviewerId;
        var title = Title;
        if (id <= 0)
        {
            try
            {
                var reviewers = await _db.GetReviewersAsync();
                var match = reviewers.FirstOrDefault(r => r.Title == title);
                if (match is not null) id = match.Id;
            }
            catch { }
        }
        var page = new ReviewerSettingsPage { ReviewerId = id, ReviewerTitle = title };
        await Navigator.PushAsync(page, Navigation);
    }

    private async void OnReviewMistakes(object? s, EventArgs e)
    {
        // Requeue items with streak == 0 and restart a fresh session
        foreach (var c in _cards)
        {
            if (c.ReviewSuccessStreak == 0)
            {
                c.Stage = Stage.Seen;
                c.DueAt = DateTime.UtcNow;
                c.CooldownUntil = DateTime.MinValue;
            }
        }
        _roundJustFinished = false;
        SessionComplete = false;
        await CompletionOverlay.FadeTo(0, 120);
        _loaded = true;
        await LoadDeckAsync();
    }

    private async void OnStudyMore(object? s, EventArgs e)
    {
        // Replay same deck with the same logic
        _roundJustFinished = false;
        SessionComplete = false;
        await CompletionOverlay.FadeTo(0, 120);
        _loaded = true;
        await LoadDeckAsync();
    }

    private async void OnAddCards(object? s, EventArgs e)
    {
        await Navigator.PushAsync(new ReviewerEditorPage { ReviewerId = ReviewerId, ReviewerTitle = Title }, Navigation);
    }

    private async void OnBackToList(object? s, EventArgs e)
    {
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseCourseToReviewers(),
            "Could not return to reviewers");
    }

    private async void OnImageTapped(object? s, TappedEventArgs e)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(FaceImage)) return;
            FullImage.Source = FaceImage;
            ImageOverlay.IsVisible = true;
            await ImageOverlay.FadeTo(1, 160, Easing.CubicOut);
        }
        catch { }
    }

    private async void OnCloseImageOverlay(object? s, TappedEventArgs e)
    {
        try
        {
            await ImageOverlay.FadeTo(0, 120, Easing.CubicIn);
            ImageOverlay.IsVisible = false;
            FullImage.Source = null;
        }
        catch { }
    }
}
