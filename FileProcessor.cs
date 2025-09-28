using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Maui.Storage;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;

namespace mindvault;

public class FileProcessor
{
    public async Task<string?> ProcessFileAsync(FileResult file)
    {
        if (file == null)
            return null;

        var ext = Path.GetExtension(file.FileName)?.ToLowerInvariant();
        try
        {
            switch (ext)
            {
                case ".txt":
                    using (var stream = await file.OpenReadAsync())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
                    {
                        var text = await reader.ReadToEndAsync();
                        return CleanAndOrganizeText(text);
                    }

                case ".pdf":
                    return await ExtractTextFromPdfAsync(file);

                default:
                    return null;
            }
        }
        catch
        {
            // Swallow and return null so UI can show a friendly error
            return null;
        }
    }

    private async Task<string?> ExtractTextFromPdfAsync(FileResult file)
    {
        // Copy picked file stream to a temporary local file accessible by iText
        var tempPath = Path.Combine(FileSystem.CacheDirectory, $"maui_pdf_{Guid.NewGuid():N}.pdf");
        using (var src = await file.OpenReadAsync())
        using (var dst = File.Create(tempPath))
        {
            await src.CopyToAsync(dst);
        }

        var sb = new StringBuilder();
        try
        {
            using var reader = new PdfReader(tempPath);
            using var pdfDoc = new PdfDocument(reader);
            int pageCount = pdfDoc.GetNumberOfPages();
            for (int i = 1; i <= pageCount; i++)
            {
                var page = pdfDoc.GetPage(i);
                // Simple text extraction strategy
                string pageText = PdfTextExtractor.GetTextFromPage(page);
                if (!string.IsNullOrWhiteSpace(pageText))
                {
                    sb.AppendLine(pageText);
                    sb.AppendLine();
                }
            }
        }
        finally
        {
            try { File.Delete(tempPath); } catch { /* ignore */ }
        }

        var raw = sb.ToString();
        return string.IsNullOrWhiteSpace(raw) ? null : CleanAndOrganizeText(raw);
    }

    private string CleanAndOrganizeText(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;

        // Normalize newlines
        var text = input.Replace("\r\n", "\n").Replace('\r', '\n');

        // Collapse 3+ newlines to just 2 (keep paragraph separation)
        text = Regex.Replace(text, "\n{3,}", "\n\n");

        // Trim whitespace on each line
        var lines = text.Split('\n').Select(l => l.Trim()).ToArray();
        text = string.Join("\n", lines);

        // Replace multiple spaces/tabs with a single space
        text = Regex.Replace(text, "[\t ]{2,}", " ");

        // Final trim
        return text.Trim();
    }
}
