using System.Text;
using System.Text.RegularExpressions;
using mindvault.Services;
using mindvault.Utils;
using mindvault.Data;
using Microsoft.Maui.Storage;

namespace mindvault.Pages;

[QueryProperty(nameof(ReviewerId), "id")]
[QueryProperty(nameof(ReviewerTitle), "title")]
public partial class SummarizeContentPage : ContentPage
{
    public int ReviewerId { get; set; }
    public string ReviewerTitle { get; set; } = string.Empty;

    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();
    readonly FileProcessor _fileProcessor = new();
    readonly FlashcardGenerator _generator = ServiceHelper.GetRequiredService<FlashcardGenerator>();

    string _rawContent = string.Empty;
    bool _suppressEditorChanged;
    int? _lastReviewerId;

    public SummarizeContentPage()
    {
        InitializeComponent();
        ContentEditor.TextChanged += OnEditorChanged;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        DeckTitleLabel.Text = ReviewerTitle;
        if (_lastReviewerId is null || _lastReviewerId != ReviewerId)
        {
            ResetState();
            _lastReviewerId = ReviewerId;
        }
    }

    void ResetState()
    {
        _rawContent = string.Empty;
        _suppressEditorChanged = true;
        ContentEditor.Text = string.Empty;
        _suppressEditorChanged = false;
        GenerateButton.IsVisible = false;
        StatusLabel.Text = string.Empty;
    }

    void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEditorChanged) return;
        _rawContent = e.NewTextValue ?? string.Empty;
        GenerateButton.IsVisible = !string.IsNullOrWhiteSpace(_rawContent);
    }

    async void OnClose(object? sender, TappedEventArgs e)
    {
        var route = $"///AddFlashcardsPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}";
        try { await Shell.Current.GoToAsync(route); } catch { await Navigation.PopAsync(); }
    }

    async void OnUploadFile(object? sender, TappedEventArgs e)
    {
        try
        {
            var pickOptions = new PickOptions
            {
                PickerTitle = "Select document",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.text", "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "org.openxmlformats.presentationml.presentation" } },
                    { DevicePlatform.Android, new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },
                    { DevicePlatform.WinUI, new[] { ".txt", ".pdf", ".docx", ".pptx" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.text", "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "org.openxmlformats.presentationml.presentation" } }
                })
            };
            var result = await FilePicker.PickAsync(pickOptions);
            if (result is null) return;

            var organizedText = await _fileProcessor.ProcessFileAsync(result);
            if (string.IsNullOrWhiteSpace(organizedText))
            {
                await DisplayAlert("File", "Failed to extract text from file.", "OK");
                return;
            }

            _rawContent = organizedText;
            _suppressEditorChanged = true;
            ContentEditor.Text = string.Empty;
            _suppressEditorChanged = false;

            GenerateButton.IsVisible = true;
            StatusLabel.Text = $"Loaded '{result.FileName}' ({_rawContent.Length} chars).";
        }
        catch (Exception ex)
        {
            await DisplayAlert("File", ex.Message, "OK");
        }
    }

    async void OnGenerate(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_rawContent)) { StatusLabel.Text = "Paste or upload content first."; return; }
        try
        {
            StatusLabel.Text = "Generating with T5...";
            var cards = await _generator.GenerateFlashcardsFromTextAsync(_rawContent);

            if (cards.Count == 0)
            {
                StatusLabel.Text = "No flashcards generated.";
                return;
            }

            // Review & edit before save
            foreach (var c in cards)
            {
                var q = await DisplayPromptAsync("Edit Question", "Review question:", initialValue: c.Question, maxLength: 256);
                if (q is null) continue; // skip if cancelled
                var a = await DisplayPromptAsync("Edit Answer", "Review answer:", initialValue: c.Answer, maxLength: 512);
                if (a is null) continue;

                await _db.AddFlashcardAsync(new mindvault.Data.Flashcard
                {
                    ReviewerId = ReviewerId,
                    Question = q.Trim(),
                    Answer = a.Trim(),
                    Learned = false,
                    Order = 0
                });
            }

            await DisplayAlert("Saved", "Flashcards added to deck.", "OK");
            await Shell.Current.GoToAsync($"///ReviewerEditorPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }
}
