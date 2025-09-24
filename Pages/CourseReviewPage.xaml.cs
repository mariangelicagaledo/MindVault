using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mindvault.Services;
using mindvault.Pages;
using mindvault.Utils;
using System.Diagnostics;
using Microsoft.Maui.Storage;
using System.Collections.Generic;
using System.Text.Json;
using CommunityToolkit.Mvvm.Messaging;
using mindvault.Utils.Messages;

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

    public int Avail => _total;
    public int Seen => _cards.Count(c => c.SeenOnce);
    public int Learned => _learnedEver.Count;
    public int Skilled => _skilledCount;
    public int Memorized => _memorizedCount;

    public string FaceTag => _front ? "[Front]" : "[Back]";
    public string FaceText => _current is null ? "" : (_front ? _current.Question : _current.Answer);
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
        _roundSize = Preferences.Get(PrefRoundSize, 10);
        _studyMode = Preferences.Get(PrefStudyMode, "Default");
        ApplyStudyMode(_studyMode);
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        WeakReferenceMessenger.Default.Register<RoundSizeChangedMessage>(this, (r, m) => { _roundSize = m.Value; _roundCount = 0; UpdateProgressWidth(); SaveProgress(); });
        WeakReferenceMessenger.Default.Register<StudyModeChangedMessage>(this, (r, m) => { _studyMode = m.Value; Preferences.Set(PrefStudyMode, m.Value); ApplyStudyMode(m.Value); SaveProgress(); });
    }

    public CourseReviewPage(string title = "Math Reviewer")
    {
        InitializeComponent();
        Title = title;
        _roundSize = Preferences.Get(PrefRoundSize, 10);
        _studyMode = Preferences.Get(PrefStudyMode, "Default");
        ApplyStudyMode(_studyMode);
        BindingContext = this;
        PageHelpers.SetupHamburgerMenu(this);
        WeakReferenceMessenger.Default.Register<RoundSizeChangedMessage>(this, (r, m) => { _roundSize = m.Value; _roundCount = 0; UpdateProgressWidth(); SaveProgress(); });
        WeakReferenceMessenger.Default.Register<StudyModeChangedMessage>(this, (r, m) => { _studyMode = m.Value; Preferences.Set(PrefStudyMode, m.Value); ApplyStudyMode(m.Value); SaveProgress(); });
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
            GraduateDays = 1; // not used in cram review; graduation uses cram first step elsewhere
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
        if (ReviewerId <= 0)
        {
            var reviewers = await _db.GetReviewersAsync();
            var match = reviewers.FirstOrDefault(r => r.Title == Title);
            if (match is not null)
                ReviewerId = match.Id;
        }

        if (_loaded) return;
        await LoadDeckAsync();
        _loaded = true;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        SaveProgress();
        WeakReferenceMessenger.Default.Unregister<RoundSizeChangedMessage>(this);
        WeakReferenceMessenger.Default.Unregister<StudyModeChangedMessage>(this);
    }

    async Task LoadDeckAsync()
    {
        _cards.Clear();
        _learnedEver.Clear();
        _skilledCount = 0;
        _memorizedCount = 0;
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
        UpdateBindingsAll();
        PickNextCard();
    }

    private void OnFlip(object? s, TappedEventArgs e)
    {
        _front = !_front;
        OnPropertyChanged(nameof(FaceTag));
        OnPropertyChanged(nameof(FaceText));
    }

    private void OnSkip(object? s, TappedEventArgs e)
    {
        _lastAnswered = _current;
        PickNextCard();
    }

    private void OnFail(object? s, TappedEventArgs e)
    {
        if (_current is null) { PickNextCard(); return; }
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

    private async void OnCloseTapped(object? s, EventArgs e)
    {
        Debug.WriteLine($"[CourseReviewPage] CloseCourseToReviewers() -> ReviewersPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.CloseCourseToReviewers(),
            "Could not return to reviewers");
    }

    private async void OnSettingsTapped(object? s, EventArgs e)
    {
        Debug.WriteLine($"[CourseReviewPage] OpenReviewerSettings() -> ReviewerSettingsPage");
        await PageHelpers.SafeNavigateAsync(this, async () => await NavigationService.OpenReviewerSettings(),
            "Could not open settings");
    }

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
        AfterPick();
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
        UpdateProgressWidth();
    }

    void UpdateProgressWidth()
    {
        double ratio = _roundSize == 0 ? 0 : Math.Clamp((double)_roundCount / _roundSize, 0, 1);
        ProgressWidth = (Width - 32) * ratio;
    }

    void IncrementRound()
    {
        _roundCount++;
        UpdateProgressWidth();
        if (_roundCount >= _roundSize && _roundSize > 0)
        {
            _roundCount = 0;
            _lastAnswered = null;
            SaveProgress();
            MainThread.BeginInvokeOnMainThread(async () =>
                await this.DisplayAlert("Round complete", $"You completed {_roundSize} questions.", "OK"));
        }
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
}
