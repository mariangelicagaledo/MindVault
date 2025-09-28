using Microsoft.ML.OnnxRuntime;
using Microsoft.Maui.Storage;
using System.Text;

namespace mindvault.Services;

public sealed class QuestionGenerationService : IAsyncDisposable
{
    private InferenceSession? _encoder;
    private InferenceSession? _decoder;

    private QuestionGenerationService() { }

    public static async Task<QuestionGenerationService> CreateAsync()
    {
        var svc = new QuestionGenerationService();
        try
        {
            // Try load T5 encoder/decoder models from app assets
            var encTmp = await CopyAssetToTempAsync("encoder_model_quantized.onnx");
            var decTmp = await CopyAssetToTempAsync("decoder_model_quantized.onnx");
            if (File.Exists(encTmp) && File.Exists(decTmp))
            {
                svc._encoder = new InferenceSession(encTmp, new SessionOptions());
                svc._decoder = new InferenceSession(decTmp, new SessionOptions());
            }
        }
        catch { /* fall back to rule-based generation */ }
        return svc;
    }

    // Primary API
    public async Task<string> GenerateQuestionAsync(string context)
    {
        // If ONNX models are not available, fall back to robust rule-based generator
        if (_encoder is null || _decoder is null)
        {
            return await Task.FromResult(FillInBlankFromContext(context));
        }

        // Minimal safe fallback even with models loaded: still use rule-based until full tokenizer is provided
        // Implementing full SentencePiece tokenization in C# is out of scope here; keep same high-quality question style
        return await Task.FromResult(FillInBlankFromContext(context));
    }

    private static string FillInBlankFromContext(string context)
    {
        var text = (context ?? string.Empty).Replace("\r\n", "\n");
        var paragraphs = text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in paragraphs)
        {
            var line = p.Trim();
            if (line.Length < 8) continue;
            var mIs = System.Text.RegularExpressions.Regex.Match(line, @"^(.+?)\s+(is|are)\s+(.+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (mIs.Success)
            {
                var cop = mIs.Groups[2].Value.Trim();
                var def = mIs.Groups[3].Value.Trim();
                var question = $"{cop} {def}".Trim();
                return question.EndsWith(".") ? question : question + ".";
            }
        }
        // Fallback if no definition-like sentence found: return first sentence chunk as a question
        var first = text.Split(new[] {'.','!','?'}, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? string.Empty;
        return string.IsNullOrEmpty(first) ? string.Empty : first + "?";
    }

    private static async Task<string> CopyAssetToTempAsync(string asset)
    {
        try
        {
            using var s = await FileSystem.OpenAppPackageFileAsync(asset);
            var path = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid():N}_{asset}");
            using var fs = File.Create(path);
            await s.CopyToAsync(fs);
            return path;
        }
        catch { return string.Empty; }
    }

    public ValueTask DisposeAsync()
    {
        try { _encoder?.Dispose(); } catch { }
        try { _decoder?.Dispose(); } catch { }
        _encoder = null; _decoder = null;
        return ValueTask.CompletedTask;
    }
}
