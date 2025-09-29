using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;

namespace mindvault.Services;

public class GeneratedFlashcard
{
    public string Question { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
}

/// <summary>
/// FlashcardGenerator orchestrates the full workflow:
/// 1) Read file (PDF/DOCX/TXT/PPTX via FileProcessor) externally.
/// 2) Clean/normalize text.
/// 3) Split into paragraphs.
/// 4) For each paragraph, use quantized T5 ONNX (encoder/decoder/decoder_with_past) to generate Q&A pairs.
/// No legacy DistilBERT or non-quantized T5 models are used here.
/// </summary>
public class FlashcardGenerator
{
    private readonly T5FlashcardService _t5;

    public FlashcardGenerator(T5FlashcardService t5)
    {
        _t5 = t5;
    }

    public async Task<List<GeneratedFlashcard>> GenerateFlashcardsFromFileAsync(FileResult fileResult)
    {
        using var s = await fileResult.OpenReadAsync();
        string rawText;
        using (var sr = new StreamReader(s))
            rawText = await sr.ReadToEndAsync();
        return await GenerateFlashcardsFromTextAsync(rawText);
    }

    public async Task<List<GeneratedFlashcard>> GenerateFlashcardsFromTextAsync(string rawText)
    {
        var text = NormalizeText(rawText);
        var paragraphs = SplitIntoParagraphs(text);

        var all = new List<GeneratedFlashcard>();
        foreach (var p in paragraphs)
        {
            if (p.Length < 24) continue;
            var generated = await _t5.GenerateQAFromParagraphAsync(p, maxNewTokens: 160);
            if (generated.Count == 0)
            {
                // Small heuristic backup in case the paragraph is too short for T5; still no DistilBERT
                var heuristic = HeuristicQA(p);
                if (heuristic is not null) generated.Add(heuristic);
            }
            all.AddRange(generated);
            if (all.Count >= 120) break; // soft cap
        }

        // Deduplicate
        var dedup = all
            .Where(fc => !string.IsNullOrWhiteSpace(fc.Question) && !string.IsNullOrWhiteSpace(fc.Answer))
            .GroupBy(fc => ($"{fc.Question}\n{fc.Answer}").Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        return dedup;
    }

    private static string NormalizeText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var t = raw.Replace("\r\n", "\n");
        t = Regex.Replace(t, @"-\s*\n", ""); // de-hyphenate line wraps
        t = Regex.Replace(t, @"\s*\n\s*", "\n"); // keep paragraph newlines
        t = Regex.Replace(t, @"\u00A0", " ");
        t = Regex.Replace(t, @"[ \t]+", " ");
        return t.Trim();
    }

    private static List<string> SplitIntoParagraphs(string text)
    {
        var paras = text.Split(new[] { "\n\n", "\n\r\n", "\r\n\r\n" }, StringSplitOptions.RemoveEmptyEntries)
                         .Select(p => p.Trim())
                         .Where(p => p.Length > 0)
                         .ToList();
        // If no double-newline paragraphs exist, split by single newline blocks roughly
        if (paras.Count <= 1)
        {
            paras = Regex.Split(text, @"\n{2,}")
                          .Select(p => p.Trim())
                          .Where(p => p.Length > 0)
                          .ToList();
        }
        return paras;
    }

    private static GeneratedFlashcard? HeuristicQA(string paragraph)
    {
        var sent = Regex.Split(paragraph, @"(?<=[\.!?])\s+").FirstOrDefault()?.Trim();
        if (string.IsNullOrWhiteSpace(sent)) return null;
        // crude fill-in-the-blank
        var m = Regex.Match(sent, @"^(.{6,40}?)(\s+is|\s+are)\s+(.{6,120})$", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            var q = $"What{m.Groups[2].Value} {m.Groups[3].Value.Trim().TrimEnd('.') }?";
            var a = m.Groups[1].Value.Trim();
            return new GeneratedFlashcard { Question = q, Answer = a };
        }
        return null;
    }
}
