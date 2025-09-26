using System.Text;
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

    readonly OfflineNerQuestionService _ner = ServiceHelper.GetRequiredService<OfflineNerQuestionService>();
    readonly DatabaseService _db = ServiceHelper.GetRequiredService<DatabaseService>();

    string _rawContent = string.Empty;

    public SummarizeContentPage()
    {
        InitializeComponent();
        ContentEditor.TextChanged += OnEditorChanged;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        DeckTitleLabel.Text = ReviewerTitle;
    }

    void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        _rawContent = e.NewTextValue ?? string.Empty;
        GenerateButton.IsVisible = !string.IsNullOrWhiteSpace(_rawContent);
    }

    async void OnBack(object? sender, TappedEventArgs e) => await Navigation.PopAsync();
    async void OnClose(object? sender, TappedEventArgs e)
    {
        // Always route back to AddFlashcardsPage for this reviewer
        var route = $"///AddFlashcardsPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}";
        try { await Shell.Current.GoToAsync(route); } catch { await Navigation.PopAsync(); }
    }

    async void OnUploadFile(object? sender, TappedEventArgs e)
    {
        try
        {
            var fileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                { DevicePlatform.Android, new[]{"application/vnd.openxmlformats-officedocument.presentationml.presentation", "text/plain"} },
                { DevicePlatform.iOS, new[]{"com.microsoft.powerpoint.pptx", "public.plain-text"} },
                { DevicePlatform.MacCatalyst, new[]{"com.microsoft.powerpoint.pptx", "public.plain-text"} },
                { DevicePlatform.WinUI, new[]{".pptx", ".txt"} },
            });
            var pick = await FilePicker.PickAsync(new PickOptions { PickerTitle = "Select PPTX or TXT", FileTypes = fileTypes });
            if (pick == null) return;
            using var stream = await pick.OpenReadAsync();
            string text;
            if (pick.FileName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(stream);
                text = await reader.ReadToEndAsync();
            }
            else // pptx basic text extraction (very naive)
            {
                text = await ExtractPptxTextAsync(stream);
            }
            ContentEditor.Text = text;
        }
        catch (Exception ex)
        {
            await DisplayAlert("File", ex.Message, "OK");
        }
    }

    async Task<string> ExtractPptxTextAsync(Stream pptxStream)
    {
        // PPTX is a zip. We parse slide*.xml and concatenate <a:t> text.
        try
        {
            using var ms = new MemoryStream();
            await pptxStream.CopyToAsync(ms);
            ms.Position = 0;
            using var archive = new System.IO.Compression.ZipArchive(ms, System.IO.Compression.ZipArchiveMode.Read, true);
            var sb = new StringBuilder();
            foreach (var entry in archive.Entries.Where(e => e.FullName.StartsWith("ppt/slides/slide") && e.FullName.EndsWith(".xml")))
            {
                using var es = entry.Open();
                using var reader = new StreamReader(es);
                var xml = await reader.ReadToEndAsync();
                int idx = 0;
                while (true)
                {
                    var open = xml.IndexOf("<a:t", idx, StringComparison.OrdinalIgnoreCase);
                    if (open == -1) break;
                    open = xml.IndexOf('>', open);
                    if (open == -1) break;
                    var close = xml.IndexOf("</a:t>", open, StringComparison.OrdinalIgnoreCase);
                    if (close == -1) break;
                    var inner = xml.Substring(open + 1, close - open - 1);
                    sb.Append(inner.Replace("&amp;", "&").Replace("&quot;", "\""));
                    sb.Append(' ');
                    idx = close + 6;
                }
            }
            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    async void OnGenerate(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_rawContent)) return;
        try
        {
            StatusLabel.Text = "Generating...";
            var qa = await _ner.GenerateFromTextAsync(ReviewerTitle, _rawContent, maxQuestions: 25);
            if (qa.Count == 0)
            {
                StatusLabel.Text = "No entities found.";
                return;
            }
            // Save cards directly then open editor
            int order = 1;
            foreach (var pair in qa)
            {
                await _db.AddFlashcardAsync(new Flashcard
                {
                    ReviewerId = ReviewerId,
                    Question = pair.Q,
                    Answer = pair.A,
                    Learned = false,
                    Order = order++
                });
            }
            StatusLabel.Text = $"Added {qa.Count} cards.";
            await Task.Delay(600);
            await Shell.Current.GoToAsync($"///ReviewerEditorPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}");
        }
        catch (Exception ex)
        {
            StatusLabel.Text = ex.Message;
        }
    }
}
