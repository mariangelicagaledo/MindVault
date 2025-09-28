using System.Text;
using System.Text.RegularExpressions;
using mindvault.Services; // still needed for ServiceHelper/DatabaseService
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

    QuestionAnsweringService? _qaService;
    QuestionGenerationService? _qgService;

    string _rawContent = string.Empty;
    bool _suppressEditorChanged;
    int? _lastReviewerId; // track last deck to reset when it changes

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
        try { ContentEditor.Text = string.Empty; } catch { }
        _suppressEditorChanged = false;
        try { GenerateButton.IsVisible = false; } catch { }
        try { StatusLabel.Text = string.Empty; } catch { }
    }

    void OnEditorChanged(object? sender, TextChangedEventArgs e)
    {
        if (_suppressEditorChanged) return;
        _rawContent = e.NewTextValue ?? string.Empty;
        GenerateButton.IsVisible = !string.IsNullOrWhiteSpace(_rawContent);
    }

    async void OnBack(object? sender, TappedEventArgs e) => await Navigation.PopAsync();
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
                PickerTitle = "Select PDF or TXT",
                FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
                {
                    { DevicePlatform.iOS, new[] { "public.text", "com.adobe.pdf" } },
                    { DevicePlatform.Android, new[] { "text/plain", "application/pdf" } },
                    { DevicePlatform.WinUI, new[] { ".txt", ".pdf" } },
                    { DevicePlatform.MacCatalyst, new[] { "public.text", "com.adobe.pdf" } }
                })
            };

            var result = await FilePicker.PickAsync(pickOptions);
            if (result is null) return;

            var organizedText = await _fileProcessor.ProcessFileAsync(result);
            if (string.IsNullOrWhiteSpace(organizedText))
            {
                await DisplayAlert("File", "Failed to extract text from the selected file.", "OK");
                return;
            }

            _rawContent = organizedText;
            _suppressEditorChanged = true;
            ContentEditor.Text = string.Empty;
            _suppressEditorChanged = false;

            // Reset and log the fully processed file content for debugging (not permanent)
            await TempLog.ClearAsync();
            await TempLog.AppendAsync($"[UPLOAD] file={result.FileName} chars={_rawContent.Length}");
            await TempLog.AppendAsync("[UPLOAD] FULL_FILE_PROCESSED_START\n" + _rawContent + "\n[UPLOAD] FULL_FILE_PROCESSED_END");

            GenerateButton.IsVisible = true;
            StatusLabel.Text = $"Loaded and formatted '{result.FileName}' ({_rawContent.Length} chars). Log: {TempLog.GetLogPath()}";
        }
        catch (Exception ex)
        {
            await DisplayAlert("File", ex.Message, "OK");
        }
    }

    async void OnGenerate(object? sender, TappedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_rawContent)) { StatusLabel.Text = $"Paste or upload content first. Log: {TempLog.GetLogPath()}"; return; }
        try
        {
            await TempLog.AppendAsync("[GENERATE] Tapped");
            StatusLabel.Text = "Loading AI...";
            _qaService ??= await QuestionAnsweringService.CreateAsync();
            _qgService ??= await QuestionGenerationService.CreateAsync();
            await TempLog.AppendAsync("[GENERATE] AI loaded");

            // Full formatted content that the splitter will use
            var formatted = NormalizeText(_rawContent);
            await TempLog.AppendAsync($"[GENERATE] formatted len={formatted.Length}");
            await TempLog.AppendAsync("[FORMATTED_FULL_START]\n" + formatted + "\n[FORMATTED_FULL_END]");

            StatusLabel.Text = "Splitting content...";
            var chunks = SplitTextIntoChunks(formatted, maxTokens: 430, minTokens: 60);
            await TempLog.AppendAsync($"[GENERATE] chunks={chunks.Count}");

            // Log every split (full text) so we see exactly what the AIs get
            var sbSplits = new StringBuilder();
            sbSplits.AppendLine("[SPLITS_FULL_START]");
            for (int i = 0; i < chunks.Count; i++)
            {
                var c = chunks[i];
                sbSplits.AppendLine($"===== CHUNK {i + 1} LEN={c.Length} =====");
                sbSplits.AppendLine(c);
            }
            sbSplits.AppendLine("[SPLITS_FULL_END]");
            await TempLog.AppendAsync(sbSplits.ToString());

            if (chunks.Count == 0) { StatusLabel.Text = $"Nothing to process. Log: {TempLog.GetLogPath()}"; return; }

            // Configure how many questions per split
            const int questionsPerChunk = 3; // increase/decrease as needed for debugging
            var seenQuestions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            int created = 0, order = 1, idx = 0;
            foreach (var chunk in chunks)
            {
                idx++;
                await TempLog.AppendAsync($"[CHUNK {idx}] LEN={chunk.Length}");

                for (int qn = 1; qn <= questionsPerChunk; qn++)
                {
                    // Generate question
                    var question = await _qgService!.GenerateQuestionAsync(chunk);
                    await TempLog.AppendAsync($"[QG {idx}.{qn}] -> {question}");
                    if (string.IsNullOrWhiteSpace(question))
                    {
                        await TempLog.AppendAsync($"[QG {idx}.{qn}] Empty question, skipping.");
                        continue;
                    }

                    var qKey = question.Trim();
                    if (!seenQuestions.Add(qKey))
                    {
                        await TempLog.AppendAsync($"[QG {idx}.{qn}] Duplicate question across splits, skipping.");
                        continue;
                    }

                    // Answer with DistilBERT using same chunk as context
                    var answer = await _qaService!.AnswerQuestionAsync(chunk, question);
                    await TempLog.AppendAsync($"[QA {idx}.{qn}] -> {answer}");
                    if (string.IsNullOrWhiteSpace(answer) || answer.StartsWith("[No answer", StringComparison.OrdinalIgnoreCase))
                    {
                        await TempLog.AppendAsync($"[QA {idx}.{qn}] Could not answer, skipping card.");
                        continue;
                    }

                    await _db.AddFlashcardAsync(new mindvault.Data.Flashcard
                    {
                        ReviewerId = ReviewerId,
                        Question = question,
                        Answer = answer,
                        Learned = false,
                        Order = order++
                    });
                    created++;

                    if (order > 60) break; // cap total
                }

                if (order > 60) break; // stop outer loop when cap reached
            }

            StatusLabel.Text = created > 0 ? $"Added {created} cards. Log: {TempLog.GetLogPath()}" : $"No flashcards generated. Log: {TempLog.GetLogPath()}";
            await TempLog.AppendAsync($"[DONE] created={created}");
            if (created > 0)
                await Shell.Current.GoToAsync($"///ReviewerEditorPage?id={ReviewerId}&title={Uri.EscapeDataString(ReviewerTitle)}");
        }
        catch (Exception ex)
        {
            await TempLog.AppendAsync($"[ERROR] {ex}");
            StatusLabel.Text = $"{ex.Message} Log: {TempLog.GetLogPath()}";
        }
    }

    static string NormalizeText(string raw)
    {
        var cleaned = Regex.Replace(raw, @"-\s*\n", "");  // join hyphenated line breaks
        cleaned = Regex.Replace(cleaned, @"\s*\n\s*", " "); // unify newlines to spaces
        cleaned = Regex.Replace(cleaned, @" +", " ");        // collapse spaces
        return cleaned.Trim();
    }

    static List<string> SplitTextIntoChunks(string text, int maxTokens = 430, int minTokens = 60)
    {
        var sentences = Regex.Split(text, @"(?<=[\.!?])\s+")
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        var chunks = new List<string>();
        var current = new StringBuilder();
        int tokens = 0;

        foreach (var s in sentences)
        {
            int sTokens = EstimateTokenCount(s);
            if (tokens + sTokens > maxTokens && tokens >= minTokens)
            {
                chunks.Add(current.ToString().Trim());
                current.Clear();
                tokens = 0;
            }
            current.Append(s).Append(' ');
            tokens += sTokens;
        }
        var last = current.ToString().Trim();
        if (!string.IsNullOrEmpty(last))
        {
            if (EstimateTokenCount(last) < minTokens && chunks.Count > 0)
                chunks[chunks.Count - 1] = (chunks[chunks.Count - 1] + " " + last).Trim();
            else
                chunks.Add(last);
        }
        if (chunks.Count == 0 && text.Length > 0)
            chunks.Add(text.Trim());
        return chunks;
    }

    static int EstimateTokenCount(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return 0;
        var basic = Regex.Matches(s, @"\w+|[^\s\w]").Count;
        return (int)Math.Ceiling(basic * 1.1) + 4; // inflate to account for subword and special tokens
    }

    static string Truncate(string s, int len) => string.IsNullOrEmpty(s) ? s : (s.Length <= len ? s : s[..len] + "...");
}
