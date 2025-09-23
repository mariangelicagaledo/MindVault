using mindvault.Services;
using mindvault.Data;
using Microsoft.Maui.Media;

namespace mindvault.Pages;

public partial class HostJudgePage : ContentPage
{
    private readonly MultiplayerService _multi = Services.ServiceHelper.GetRequiredService<MultiplayerService>();
    private readonly DatabaseService _db = Services.ServiceHelper.GetRequiredService<DatabaseService>();

    private string _currentQuestion = string.Empty;
    private string _currentAnswer = string.Empty;
    private string _buzzWinnerName = string.Empty;
    private string _buzzWinnerAvatar = "avatar1.png";
    private bool _showAnswer;
    private bool _hasBuzzWinner;
    private readonly bool _startRematch;

    public string CurrentQuestion { get => _currentQuestion; set { _currentQuestion = value; OnPropertyChanged(); } }
    public string CurrentAnswer { get => _currentAnswer; set { _currentAnswer = value; OnPropertyChanged(); } }
    public string BuzzWinner { get => _buzzWinnerName; set { _buzzWinnerName = value; OnPropertyChanged(); HasBuzzWinner = !string.IsNullOrEmpty(_buzzWinnerName); } }
    public string BuzzAvatar { get => _buzzWinnerAvatar; set { _buzzWinnerAvatar = value; OnPropertyChanged(); } }
    public bool HasBuzzWinner { get => _hasBuzzWinner; set { if (_hasBuzzWinner == value) return; _hasBuzzWinner = value; OnPropertyChanged(); } }

    private int _reviewerId;
    private List<Flashcard> _cards = new();
    private int _index = -1;

    public HostJudgePage(int reviewerId, string title) : this(reviewerId, title, startRematch: false) { }

    public HostJudgePage(int reviewerId, string title, bool startRematch)
    {
        InitializeComponent();
        BindingContext = this;
        _reviewerId = reviewerId;
        _startRematch = startRematch;

        _multi.HostBuzzWinner += OnHostBuzzWinner;
        _multi.HostGameOverOccurred += OnHostGameOver;
        _deckTitle = title;

        _multi.HostSetCurrentDeck(reviewerId, title);
    }

    private string _deckTitle = string.Empty;

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await LoadDeckAsync();
        NextCard();
        if (_startRematch)
        {
            // Delay slightly to ensure UI is ready
            await Task.Delay(50);
            try { _multi.HostStartRematch(); } catch { }
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _multi.HostBuzzWinner -= OnHostBuzzWinner;
        _multi.HostGameOverOccurred -= OnHostGameOver;
    }

    private async Task LoadDeckAsync()
    {
        try
        {
            _cards = await _db.GetFlashcardsAsync(_reviewerId);
            _cards = _cards.OrderBy(c => c.Order).ToList();
        }
        catch
        {
            _cards = new();
        }
    }

    private void NextCard()
    {
        _index++;
        _multi.OpenBuzzForAll();
        BuzzWinner = string.Empty; // hides avatar/name
        _showAnswer = false;
        if (_index >= 0 && _index < _cards.Count)
        {
            CurrentQuestion = _cards[_index].Question;
            CurrentAnswer = string.Empty;
        }
        else
        {
            CurrentQuestion = "No more cards.";
            CurrentAnswer = string.Empty;
            _multi.HostGameOver(_deckTitle);
            return;
        }
        _multi.UpdateQuestionState(Math.Min(_index + 1, Math.Max(_cards.Count, 1)), _cards.Count);
    }

    private void OnHostBuzzWinner(MultiplayerService.ParticipantInfo p)
    {
        MainThread.BeginInvokeOnMainThread(() => { BuzzWinner = p.Name; BuzzAvatar = string.IsNullOrEmpty(p.Avatar) ? "avatar1.png" : p.Avatar; _lastWinnerId = p.Id; });
    }

    private void OnHostGameOver(MultiplayerService.GameOverPayload payload)
    {
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            await Navigation.PushAsync(new GameOverPage(payload));
        });
    }

    private string _lastWinnerId = string.Empty;

    private void OnFlip(object? s, TappedEventArgs e)
    {
        _showAnswer = !_showAnswer;
        if (_showAnswer)
        {
            if (_index >= 0 && _index < _cards.Count)
                CurrentAnswer = _cards[_index].Answer;
        }
        else
        {
            CurrentAnswer = string.Empty;
        }
    }

    private void OnSkip(object? s, TappedEventArgs e)
    {
        if (_index >= 0 && _index < _cards.Count)
        {
            var cur = _cards[_index];
            _cards.RemoveAt(_index);
            _cards.Add(cur);
            _index--;
        }
        NextCard();
    }

    private void OnAccept(object? s, TappedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastWinnerId))
        {
            _multi.HostStopTimerFor(_lastWinnerId);
            _multi.HostAwardPoint(_lastWinnerId, +1);
            if (_index >= 0 && _index < _cards.Count)
            {
                var answer = _cards[_index].Answer ?? string.Empty;
                _multi.HostAnnounceCorrectAnswer(answer);
            }
        }
        NextCard();
    }

    private async void OnReject(object? s, TappedEventArgs e)
    {
        if (!string.IsNullOrEmpty(_lastWinnerId))
            _multi.HostStopTimerFor(_lastWinnerId);
        await _multi.ReopenBuzzExceptWinnerAsync();
        BuzzWinner = string.Empty; // let others buzz
    }

    private async void OnSpeakTapped(object? s, TappedEventArgs e)
    {
        var text = string.IsNullOrWhiteSpace(CurrentQuestion) ? "" : CurrentQuestion;
        if (!string.IsNullOrEmpty(text))
        {
            try { await TextToSpeech.Default.SpeakAsync(text); } catch { }
        }
    }
}
