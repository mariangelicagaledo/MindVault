namespace mindvault;

using mindvault.Services;

public partial class MainPage : ContentPage
{
    private readonly FileProcessor _fileProcessor = new();
    private QuestionAnsweringService? _qaService;
    private QuestionGenerationService? _qgService;
    private FlashcardGenerator? _flashcardGenerator;
    private bool _isAiReady;

    public MainPage()
    {
        InitializeComponent();
        InitializeServices();
    }

    private async void InitializeServices()
    {
        try
        {
            _qaService = await QuestionAnsweringService.CreateAsync();
            _qgService = await QuestionGenerationService.CreateAsync();
            _flashcardGenerator = new FlashcardGenerator(_qgService, _qaService);
            _isAiReady = true;
        }
        catch (Exception ex)
        {
            _isAiReady = false;
            await DisplayAlert("AI", $"Failed to initialize AI: {ex.Message}", "OK");
        }
    }

    private async void OnPickFileClicked(object sender, EventArgs e)
    {
        var pickOptions = new PickOptions
        {
            PickerTitle = "Please select a document (.txt or .pdf)",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { "public.text", "com.adobe.pdf" } },
                { DevicePlatform.Android, new[] { "text/plain", "application/pdf" } },
                { DevicePlatform.WinUI, new[] { ".txt", ".pdf" } },
                { DevicePlatform.macOS, new[] { "public.text", "com.adobe.pdf" } }
            })
        };

        try
        {
            var result = await FilePicker.PickAsync(pickOptions);
            if (result != null)
            {
                var ext = Path.GetExtension(result.FileName)?.ToLowerInvariant();
                if (ext is not ".txt" and not ".pdf")
                {
                    await DisplayAlert("Unsupported", "Only .txt and .pdf files are supported.", "OK");
                    return;
                }

                ContentEditor.Text = "Processing file, please wait...";
                string? organizedText = await _fileProcessor.ProcessFileAsync(result);
                ContentEditor.Text = organizedText ?? "Failed to extract text from the file.";

                if (_isAiReady && _flashcardGenerator is not null)
                {
                    var cards = await _flashcardGenerator.GenerateFlashcardsFromFileAsync(result);
                    if (cards.Count > 0)
                    {
                        var first = cards[0];
                        await DisplayAlert("Flashcard", $"Q: {first.Question}\n\nA: {first.Answer}", "OK");
                    }
                    else
                    {
                        await DisplayAlert("Result", "No flashcards could be generated.", "OK");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            ContentEditor.Text = "An error occurred during file processing.";
        }
    }
}
