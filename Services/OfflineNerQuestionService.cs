using System.Text.Json;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Linq;

namespace mindvault.Services;

// Simple offline NER + question generation from raw text using a DistilBERT NER model.
// Assets expected under Resources/Raw/models/distilbert-ner
public class OfflineNerQuestionService : IAsyncDisposable
{
    readonly Lazy<Task> _loadTask;
    InferenceSession? _session; // will be set after InitializeAsync
    string[] _id2label = Array.Empty<string>();
    Dictionary<string,int> _vocab = new();
    const int MaxLen = 128; // keep small for mobile

    public OfflineNerQuestionService() => _loadTask = new Lazy<Task>(InitializeAsync);

    public Task EnsureLoadedAsync() => _loadTask.Value;

    async Task InitializeAsync()
    {
        // Load labels
        using var labStream = await FileSystem.OpenAppPackageFileAsync("models/distilbert-ner/labels.txt");
        using var labReader = new StreamReader(labStream);
        var labels = new List<string>();
        while (!labReader.EndOfStream)
        {
            var line = (await labReader.ReadLineAsync())?.Trim();
            if (!string.IsNullOrWhiteSpace(line)) labels.Add(line);
        }
        _id2label = labels.ToArray();

        // Load tokenizer (vocab)
        using var tokStream = await FileSystem.OpenAppPackageFileAsync("models/distilbert-ner/tokenizer.json");
        using var doc = await JsonDocument.ParseAsync(tokStream);
        if (doc.RootElement.TryGetProperty("model", out var modelEl) && modelEl.TryGetProperty("vocab", out var vocabEl))
        {
            foreach (var prop in vocabEl.EnumerateObject())
                _vocab[prop.Name] = prop.Value.GetInt32();
        }

        // Copy model to temp path (InferenceSession needs file or bytes)
        using var modelStream = await FileSystem.OpenAppPackageFileAsync("models/distilbert-ner/model.onnx");
        var tmpPath = Path.Combine(FileSystem.CacheDirectory, "distilbert-ner.onnx");
        using (var fs = File.Open(tmpPath, FileMode.Create, FileAccess.Write))
            await modelStream.CopyToAsync(fs);

        // Use default session options (avoid GPU specifics for mobile)
        _session = new InferenceSession(tmpPath);
    }

    // Generate (Question, Answer) list from text using detected entities.
    public async Task<List<(string Q, string A)>> GenerateFromTextAsync(string title, string text, int maxQuestions = 20)
    {
        await EnsureLoadedAsync();
        if (_session is null) return new();

        var sentences = SplitSentences(text).Take(50).ToList();
        var qa = new List<(string Q, string A)>();
        foreach (var sent in sentences)
        {
            var entities = GetEntities(sent);
            foreach (var e in entities)
            {
                var q = BuildQuestion(e.label, sent);
                if (!string.IsNullOrWhiteSpace(q))
                {
                    qa.Add((q, e.text));
                    if (qa.Count >= maxQuestions) return qa;
                }
            }
        }
        return qa;
    }

    string BuildQuestion(string label, string sentence)
    {
        sentence = sentence.Trim();
        return label switch
        {
            "PER" or "B-PER" or "I-PER" => $"Who is referenced in: {sentence}?",
            "ORG" or "B-ORG" or "I-ORG" => $"Which organization is mentioned here: {sentence}?",
            "LOC" or "B-LOC" or "I-LOC" => $"Which location is mentioned in: {sentence}?",
            "MISC" or "B-MISC" or "I-MISC" => $"Which named entity appears in: {sentence}?",
            _ => string.Empty
        };
    }

    List<(string text,string label)> GetEntities(string sentence)
    {
        var list = new List<(string text,string label)>();
        var (inputIds, attentionMask) = Tokenize(sentence);
        if (inputIds.Length == 0 || _session is null) return list;

        var idTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
        var maskTensor = new DenseTensor<long>(new[] { 1, inputIds.Length });
        for (int i = 0; i < inputIds.Length; i++) { idTensor[0, i] = inputIds[i]; maskTensor[0, i] = attentionMask[i]; }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", idTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", maskTensor)
        };

        try
        {
            using var results = _session.Run(inputs);
            var first = results.First();
            var logits = first.AsTensor<float>();
            var dims = logits.Dimensions; // int[]
            if (dims.Length != 3) return list;
            int seq = dims[1];
            int labels = dims[2];
            var spans = new List<(int start,int end,string label)>();
            string currentLabel = string.Empty; int startIdx = -1;
            for (int t = 0; t < seq; t++)
            {
                if (t >= inputIds.Length) break;
                int bestIdx = ArgMax(logits, t, labels);
                string raw = bestIdx >= 0 && bestIdx < _id2label.Length ? _id2label[bestIdx] : "O";
                if (raw.StartsWith("B-"))
                {
                    if (startIdx != -1) spans.Add((startIdx, t - 1, currentLabel));
                    currentLabel = raw.Substring(2);
                    startIdx = t;
                }
                else if (raw.StartsWith("I-")) { /* continue span */ }
                else // O
                {
                    if (startIdx != -1) { spans.Add((startIdx, t - 1, currentLabel)); startIdx = -1; }
                }
            }
            if (startIdx != -1) spans.Add((startIdx, seq - 1, currentLabel));

            foreach (var sp in spans)
            {
                var tokens = ReconstructTokens(sentence, sp.start, sp.end);
                if (!string.IsNullOrWhiteSpace(tokens)) list.Add((tokens, sp.label));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OfflineNerQuestionService] inference error: {ex.Message}");
        }
        return list;
    }

    int ArgMax(Tensor<float> logits, int position, int labelCount)
    {
        float best = float.MinValue; int idx = 0;
        for (int l = 0; l < labelCount; l++)
        {
            var v = logits[0, position, l];
            if (v > best) { best = v; idx = l; }
        }
        return idx;
    }

    (long[] ids, long[] mask) Tokenize(string text)
    {
        var tokens = BasicTokenize(text).Take(MaxLen - 2).ToList();
        var ids = new List<long>();
        var mask = new List<long>();
        ids.Add(GetId("[CLS]")); mask.Add(1);
        foreach (var tk in tokens) { ids.Add(GetId(tk)); mask.Add(1); }
        ids.Add(GetId("[SEP]")); mask.Add(1);
        return (ids.ToArray(), mask.ToArray());
    }

    IEnumerable<string> BasicTokenize(string text)
    {
        var raw = text.Split(new[]{' ','\t','\n','\r'}, StringSplitOptions.RemoveEmptyEntries);
        foreach (var r in raw)
        {
            var cleaned = r.Trim();
            if (cleaned.Length==0) continue;
            if (_vocab.ContainsKey(cleaned)) yield return cleaned;
            else if (_vocab.ContainsKey(cleaned.ToLowerInvariant())) yield return cleaned.ToLowerInvariant();
            else yield return "[UNK]";
        }
    }

    long GetId(string token) => _vocab.TryGetValue(token, out var id) ? id : (_vocab.TryGetValue("[UNK]", out var unk) ? unk : 100);

    string ReconstructTokens(string originalSentence, int start, int end)
    {
        var words = originalSentence.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (start < 0 || end >= words.Length || start > end) return string.Empty;
        return string.Join(' ', words.Skip(start).Take(end - start + 1));
    }

    IEnumerable<string> SplitSentences(string text)
    {
        var parts = text.Split(new[]{'.','!','?'}, StringSplitOptions.RemoveEmptyEntries);
        foreach (var p in parts)
        {
            var s = p.Trim();
            if (s.Length > 3) yield return s + ".";
        }
    }

    public ValueTask DisposeAsync()
    {
        _session?.Dispose();
        return ValueTask.CompletedTask;
    }
}
