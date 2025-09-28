using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;
using iText.Kernel.Pdf.Canvas.Parser.Listener;

namespace mindvault.Services;

public class GeneratedFlashcard
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

public class FlashcardGenerator
{
    private readonly QuestionGenerationService _qgService;
    private readonly QuestionAnsweringService _qaService;

    public FlashcardGenerator(QuestionGenerationService qgService, QuestionAnsweringService qaService)
    {
        _qgService = qgService;
        _qaService = qaService;
    }

    public async Task<List<GeneratedFlashcard>> GenerateFlashcardsFromFileAsync(FileResult fileResult)
    {
        var formattedText = await SmartFormatFileContentAsync(fileResult);
        if (string.IsNullOrWhiteSpace(formattedText)) return new();
        return await GenerateFlashcardsFromTextAsync(formattedText);
    }

    public async Task<List<GeneratedFlashcard>> GenerateFlashcardsFromTextAsync(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return new();
        var formattedText = SmartFormatPlainText(rawText);
        var sentences = SplitTextIntoSentences(formattedText);
        var flashcards = new List<GeneratedFlashcard>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        int idx = 0;
        foreach (var sentence in sentences)
        {
            idx++;
            if (sentence.Length < 20) continue;

            var question = await _qgService.GenerateQuestionAsync(sentence);
            if (string.IsNullOrWhiteSpace(question)) continue;
            if (!seen.Add(question.Trim())) continue; // skip duplicates

            var answer = await _qaService.AnswerQuestionAsync(sentence, question);
            if (!string.IsNullOrWhiteSpace(answer) && !answer.StartsWith("[No answer", StringComparison.OrdinalIgnoreCase))
            {
                flashcards.Add(new GeneratedFlashcard { Question = question.Trim(), Answer = answer.Trim() });
                if (flashcards.Count >= 100) break; // safety cap
            }
        }
        return flashcards;
    }

    private static async Task<string> SmartFormatFileContentAsync(FileResult fileResult)
    {
        string rawText;
        using (var stream = await fileResult.OpenReadAsync())
        {
            if (fileResult.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new PdfReader(stream);
                using var pdf = new PdfDocument(reader);
                var sb = new StringBuilder();
                for (int i = 1; i <= pdf.GetNumberOfPages(); i++)
                {
                    ITextExtractionStrategy strategy = new SimpleTextExtractionStrategy();
                    sb.AppendLine(PdfTextExtractor.GetTextFromPage(pdf.GetPage(i), strategy));
                }
                rawText = sb.ToString();
            }
            else
            {
                using var sr = new StreamReader(stream);
                rawText = await sr.ReadToEndAsync();
            }
        }
        return SmartFormatPlainText(rawText);
    }

    private static string SmartFormatPlainText(string rawText)
    {
        if (string.IsNullOrWhiteSpace(rawText)) return string.Empty;

        var proseLines = new List<string>();
        foreach (var line in rawText.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5)
                proseLines.Add(trimmed);
        }
        var text = string.Join(' ', proseLines);

        text = Regex.Replace(text, @"-\s+", "");
        text = Regex.Replace(text, @"\s+", " ");
        text = Regex.Replace(text, @"(?<=[a-z])\.(?=[A-Z])", ". ");
        return text.Trim();
    }

    private static List<string> SplitTextIntoSentences(string text)
    {
        return Regex.Split(text, @"(?<=[\.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length > 20 && s.Count(c => char.IsLetterOrDigit(c)) >= 10)
            .Take(500)
            .ToList();
    }
}
