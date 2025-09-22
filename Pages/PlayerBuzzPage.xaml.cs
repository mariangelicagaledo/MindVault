using System.Collections.ObjectModel;
using mindvault.Services;
using Plugin.Maui.Audio;

namespace mindvault.Pages;

public partial class PlayerBuzzPage : ContentPage
{
    public ObservableCollection<Score> Scores { get; } = new();

    private readonly MultiplayerService _multi = Services.ServiceHelper.GetRequiredService<MultiplayerService>();
    private readonly Dictionary<string, int> _scoreMap = new();
    private readonly Dictionary<string, string> _avatars = new();
    private bool _canBuzz = true;

    private CancellationTokenSource? _timerCts;
    private Border? _timerPanel;
    private Label? _timerLabel;
    private Label? _questionBadge;

    private IAudioPlayer? _tickPlayer;
    private IAudioPlayer? _timeupPlayer;
    private IAudioPlayer? _wrongPlayer;
    private Stream? _tickStream;
    private Stream? _timeupStream;
    private Stream? _wrongStream;

    public PlayerBuzzPage()
    {
        InitializeComponent();
        BindingContext = this;

        _timerPanel = this.FindByName<Border>("TimerPanel");
        _timerLabel = this.FindByName<Label>("TimerLabel");
        _questionBadge = this.FindByName<Label>("QuestionBadge");

        _multi.ClientParticipantJoined += OnClientParticipantJoined;
        _multi.ClientBuzzingStarted += OnBuzzingStarted;
        _multi.ClientBuzzReset += OnBuzzReset;
        _multi.ClientScoreUpdated += OnScoreUpdated;
        _multi.ClientBuzzerEnabledChanged += OnBuzzerEnabledChanged;
        _multi.ClientQuestionStateChanged += OnQuestionStateChanged;
        _multi.ClientTimeUp += OnClientTimeUp;
        _multi.ClientStopTimer += OnClientStopTimer;
        _multi.ClientCorrectAnswer += OnClientCorrectAnswer;
        _multi.ClientWrong += OnClientWrong;
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _multi.ClientParticipantJoined -= OnClientParticipantJoined;
        _multi.ClientBuzzingStarted -= OnBuzzingStarted;
        _multi.ClientBuzzReset -= OnBuzzReset;
        _multi.ClientScoreUpdated -= OnScoreUpdated;
        _multi.ClientBuzzerEnabledChanged -= OnBuzzerEnabledChanged;
        _multi.ClientQuestionStateChanged -= OnQuestionStateChanged;
        _multi.ClientTimeUp -= OnClientTimeUp;
        _multi.ClientStopTimer -= OnClientStopTimer;
        _multi.ClientCorrectAnswer -= OnClientCorrectAnswer;
        _multi.ClientWrong -= OnClientWrong;
        StopTimerUI();
    }

    private void OnClientParticipantJoined(MultiplayerService.ParticipantInfo p)
    {
        if (!string.IsNullOrEmpty(p.Id))
        {
            _avatars[p.Id] = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar;
        }
    }

    private void OnClientWrong(string id, string name)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await PlayWrongAsync();
            await DisplayAlert("Wrong Answer", $"{name} is wrong! Steal the question!", "OK");
        });
    }

    private void OnClientCorrectAnswer(string text)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await DisplayAlert("Correct Answer", text, "OK");
        });
    }

    private void OnQuestionStateChanged(int idx, int total)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_questionBadge is not null)
                _questionBadge.Text = total > 0 ? $"Question {idx} of {total}" : "";
        });
    }

    private void OnBuzzingStarted(string id, string name, long deadlineTicks)
    {
        var remaining = TimeSpan.FromTicks(Math.Max(0, deadlineTicks - DateTime.UtcNow.Ticks));
        StartCountdown(remaining <= TimeSpan.Zero ? TimeSpan.FromSeconds(10) : remaining);
    }

    private void OnClientStopTimer(string id)
    {
        StopTimerUI();
    }

    private async void OnClientTimeUp(string id)
    {
        await PlayTimeupAsync();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_timerLabel is not null) _timerLabel.Text = "0.0s";
            if (_timerPanel is not null) _timerPanel.IsVisible = true;
        });
    }

    private void StopTimerUI()
    {
        StopTimer();
        StopTickSound();
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_timerPanel is not null) _timerPanel.IsVisible = false;
        });
    }

    private void OnBuzzerEnabledChanged(string id, bool enabled)
    {
        if (id == "*")
        {
            _canBuzz = enabled;
            StopTimerUI();
            MainThread.BeginInvokeOnMainThread(() => Bell.Opacity = enabled ? 1.0 : 0.5);
            return;
        }

        if (string.Equals(id, _multi.SelfId, StringComparison.Ordinal))
        {
            _canBuzz = enabled;
            MainThread.BeginInvokeOnMainThread(() => Bell.Opacity = enabled ? 1.0 : 0.5);
        }
    }

    private void OnBuzzReset()
    {
        StopTimerUI();
    }

    private void StartCountdown(TimeSpan duration)
    {
        StopTimer();
        _timerCts = new CancellationTokenSource();
        var ct = _timerCts.Token;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (_timerPanel is not null) _timerPanel.IsVisible = true;
            if (_timerLabel is not null) _timerLabel.Text = $"{duration.TotalSeconds:0.0}s";
        });

        _ = Task.Run(async () =>
        {
            await PlayTickAsync();

            var end = DateTime.UtcNow + duration;
            while (!ct.IsCancellationRequested)
            {
                var remaining = end - DateTime.UtcNow;
                if (remaining <= TimeSpan.Zero) break;
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    if (_timerLabel is not null)
                        _timerLabel.Text = $"{Math.Max(0, remaining.TotalSeconds):0.0}s";
                });
                try { await Task.Delay(100, ct); } catch { break; }
            }

            StopTickSound();
        }, ct);
    }

    private void StopTimer()
    {
        try { _timerCts?.Cancel(); } catch { }
        _timerCts = null;
    }

    private async Task PlayTickAsync()
    {
        try
        {
            StopTickSound();
            _tickStream = await FileSystem.OpenAppPackageFileAsync("timer.mp3");
            _tickPlayer = AudioManager.Current.CreatePlayer(_tickStream);
            _tickPlayer.Loop = true;
            _tickPlayer.Volume = 0.9;
            _tickPlayer.Play();
        }
        catch { }
    }

    private void StopTickSound()
    {
        try { _tickPlayer?.Stop(); } catch { }
        try { _tickPlayer?.Dispose(); } catch { }
        _tickPlayer = null;
        try { _tickStream?.Dispose(); } catch { }
        _tickStream = null;
    }

    private async Task PlayTimeupAsync()
    {
        try
        {
            StopTickSound();
            _timeupStream = await FileSystem.OpenAppPackageFileAsync("timesup.mp3");
            _timeupPlayer = AudioManager.Current.CreatePlayer(_timeupStream);
            _timeupPlayer.Loop = false;
            _timeupPlayer.Volume = 1.0;
            _timeupPlayer.Play();
        }
        catch { }
    }

    private async Task PlayWrongAsync()
    {
        try
        {
            _wrongStream = await FileSystem.OpenAppPackageFileAsync("wronganswer.mp3");
            _wrongPlayer = AudioManager.Current.CreatePlayer(_wrongStream);
            _wrongPlayer.Loop = false;
            _wrongPlayer.Volume = 1.0;
            _wrongPlayer.Play();
        }
        catch { }
    }

    private async void OnBellTapped(object? sender, TappedEventArgs e)
    {
        if (!_canBuzz) return;
        await RingBellAsync();
        await _multi.SendBuzzAsync();
    }

    private async Task RingBellAsync()
    {
        try
        {
            await Bell.ScaleTo(0.92, 90, Easing.CubicIn);
            await Task.WhenAll(
                Bell.ScaleTo(1.0, 180, Easing.SpringOut),
                Bell.TranslateTo(0, 0, 160, Easing.SpringOut),
                Bell.RotateTo(0, 160, Easing.SpringOut)
            );
        }
        catch { }
    }

    private void OnScoreUpdated(string id, int score)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            _scoreMap[id] = score;
            var sorted = _scoreMap.OrderByDescending(kv => kv.Value).ToList();
            Scores.Clear();
            int rank = 1;
            foreach (var kv in sorted)
            {
                var avatar = _avatars.TryGetValue(kv.Key, out var av) ? av : "avatar1.png";
                Scores.Add(new Score
                {
                    Rank = rank,
                    Name = kv.Key,
                    Points = kv.Value,
                    Image = avatar,
                    IsLeader = rank == 1
                });
                rank++;
            }
        });
    }

    public class Score
    {
        public int Rank { get; set; }
        public string Name { get; set; } = "";
        public int Points { get; set; }
        public string PointsText => $"{Points} PTS";
        public string Image { get; set; } = "avatar1.png";
        public bool IsLeader { get; set; }
    }
}
