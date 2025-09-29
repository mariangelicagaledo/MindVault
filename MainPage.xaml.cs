namespace mindvault;

using mindvault.Services;

public partial class MainPage : ContentPage
{
    private readonly FileProcessor _fileProcessor = new();
    private readonly FlashcardGenerator _flashcardGenerator = ServiceHelper.GetRequiredService<FlashcardGenerator>();

    public MainPage()
    {
        InitializeComponent();
    }

    private async void OnPickFileClicked(object sender, EventArgs e)
    {
        var pickOptions = new PickOptions
        {
            PickerTitle = "Please select a document",
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.iOS, new[] { "public.text", "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "org.openxmlformats.presentationml.presentation" } },
                { DevicePlatform.Android, new[] { "text/plain", "application/pdf", "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "application/vnd.openxmlformats-officedocument.presentationml.presentation" } },
                { DevicePlatform.WinUI, new[] { ".txt", ".pdf", ".docx", ".pptx" } },
                { DevicePlatform.macOS, new[] { "public.text", "com.adobe.pdf", "org.openxmlformats.wordprocessingml.document", "org.openxmlformats.presentationml.presentation" } }
            })
        };

        try
        {
            var result = await FilePicker.PickAsync(pickOptions);
            if (result != null)
            {
                ContentEditor.Text = "Processing file, please wait...";
                string? organizedText = await _fileProcessor.ProcessFileAsync(result);
                ContentEditor.Text = organizedText ?? "Failed to extract text from the file.";

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
        catch (Exception ex)
        {
            await DisplayAlert("Error", $"An error occurred: {ex.Message}", "OK");
            ContentEditor.Text = "An error occurred during file processing.";
        }
    }
}
