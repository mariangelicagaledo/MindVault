using System.Text;
using Microsoft.Maui.Storage;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace mindvault.Services;

public class QuestionAnsweringService : IAsyncDisposable
{
    private readonly InferenceSession _session;
    private readonly SimpleWordPieceTokenizer _tokenizer;

    private QuestionAnsweringService(InferenceSession session, SimpleWordPieceTokenizer tokenizer)
    {
        _session = session;
        _tokenizer = tokenizer;
    }

    public static async Task<QuestionAnsweringService> CreateAsync()
    {
        // Load ONNX model and vocab.txt from Maui assets
        using var modelStream = await FileSystem.OpenAppPackageFileAsync("model.onnx");
        using var vocabStream = await FileSystem.OpenAppPackageFileAsync("vocab.txt");

        // Copy model to a temp file because InferenceSession needs a file path or byte[]
        var tmpPath = Path.Combine(FileSystem.CacheDirectory, $"model_{Guid.NewGuid():N}.onnx");
        using (var fs = File.Create(tmpPath))
            await modelStream.CopyToAsync(fs);

        var tokenizer = await SimpleWordPieceTokenizer.CreateAsync(vocabStream);
        var session = new InferenceSession(tmpPath, new SessionOptions());
        return new QuestionAnsweringService(session, tokenizer);
    }

    public async Task<string> AnswerQuestionAsync(string context, string question)
    {
        var enc = await _tokenizer.EncodeAsync(question, context, 512);

        var inputIds = new DenseTensor<long>(new[] { 1, enc.InputIds.Length });
        var attention = new DenseTensor<long>(new[] { 1, enc.AttentionMask.Length });
        for (int i = 0; i < enc.InputIds.Length; i++)
        {
            inputIds[0, i] = enc.InputIds[i];
            attention[0, i] = enc.AttentionMask[i];
        }

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", inputIds),
            NamedOnnxValue.CreateFromTensor("attention_mask", attention)
        };

        try
        {
            using var results = _session.Run(inputs);
            var startTensor = results.First(x => x.Name.Contains("start", StringComparison.OrdinalIgnoreCase)).AsTensor<float>();
            var endTensor = results.First(x => x.Name.Contains("end", StringComparison.OrdinalIgnoreCase)).AsTensor<float>();

            // Tensors are [1, seqLen]
            var dims = startTensor.Dimensions.ToArray();
            int seqLen = dims.Length > 1 ? dims[1] : dims[0];
            int startIndex = ArgMax(startTensor, seqLen);
            int endIndex = ArgMax(endTensor, seqLen);

            if (startIndex <= 0 || endIndex <= 0 || endIndex < startIndex)
                return "[No answer found]";

            // Map back to tokens and decode
            var answerTokens = enc.Tokens.Skip(startIndex).Take(endIndex - startIndex + 1).ToList();
            var answer = _tokenizer.Decode(answerTokens);
            return string.IsNullOrWhiteSpace(answer) ? "[No answer found]" : answer;
        }
        catch (Exception ex)
        {
            return $"[ONNX error] {ex.Message}";
        }
    }

    private static int ArgMax(Tensor<float> t, int seqLen)
    {
        int best = 0; float bestVal = float.MinValue;
        for (int i = 0; i < seqLen; i++)
        {
            var v = t[0, i];
            if (v > bestVal) { bestVal = v; best = i; }
        }
        return best;
    }

    public ValueTask DisposeAsync()
    {
        _session.Dispose();
        return ValueTask.CompletedTask;
    }
}

internal sealed class SimpleWordPieceTokenizer
{
    private readonly Dictionary<string, int> _vocab;
    private readonly string _cls = "[CLS]";
    private readonly string _sep = "[SEP]";
    private readonly string _pad = "[PAD]";
    private readonly string _unk = "[UNK]";

    private readonly int _clsId;
    private readonly int _sepId;
    private readonly int _padId;
    private readonly int _unkId;

    private SimpleWordPieceTokenizer(Dictionary<string, int> vocab)
    {
        _vocab = vocab;
        _clsId = GetId(_cls, 101);
        _sepId = GetId(_sep, 102);
        _padId = GetId(_pad, 0);
        _unkId = GetId(_unk, 100);
    }

    private int GetId(string token, int fallback)
        => _vocab.TryGetValue(token, out var id) ? id : fallback;

    public static async Task<SimpleWordPieceTokenizer> CreateAsync(Stream vocabStream)
    {
        using var sr = new StreamReader(vocabStream, Encoding.UTF8, leaveOpen: true);
        var vocab = new Dictionary<string, int>(StringComparer.Ordinal);
        string? line; int idx = 0;
        while ((line = await sr.ReadLineAsync()) is not null)
        {
            if (!vocab.ContainsKey(line)) vocab[line] = idx;
            idx++;
        }
        return new SimpleWordPieceTokenizer(vocab);
    }

    public async Task<(int[] InputIds, int[] AttentionMask, List<string> Tokens)> EncodeAsync(string question, string context, int maxLen)
    {
        // DistilBERT-cased: keep case; perform basic tokenization
        var qTokens = BasicTokenize(question);
        var cTokens = BasicTokenize(context);

        // WordPiece tokenize
        qTokens = qTokens.SelectMany(WordPieceTokenize).ToList();
        cTokens = cTokens.SelectMany(WordPieceTokenize).ToList();

        // Build sequence: [CLS] q [SEP] c [SEP]
        var tokens = new List<string>(maxLen) { _cls };
        tokens.AddRange(qTokens);
        tokens.Add(_sep);

        int available = maxLen - 1 - tokens.Count; // space for final [SEP]
        if (available < 0) available = 0;

        if (cTokens.Count > available)
        {
            cTokens = cTokens.Take(available).ToList();
        }
        tokens.AddRange(cTokens);
        tokens.Add(_sep);

        // Convert to ids
        var ids = tokens.Select(t => _vocab.TryGetValue(t, out var id) ? id : _unkId).ToList();

        // Pad if needed
        if (ids.Count < maxLen)
        {
            int padCount = maxLen - ids.Count;
            for (int i = 0; i < padCount; i++)
            {
                ids.Add(_padId);
                tokens.Add(_pad);
            }
        }
        else if (ids.Count > maxLen)
        {
            ids = ids.Take(maxLen).ToList();
            tokens = tokens.Take(maxLen).ToList();
        }

        var mask = ids.Select(id => id == _padId ? 0 : 1).ToArray();
        return (ids.ToArray(), mask, tokens);
    }

    public string Decode(IEnumerable<string> tokens)
    {
        var sb = new StringBuilder();
        bool first = true;
        foreach (var tok in tokens)
        {
            if (tok is "[CLS]" or "[SEP]" or "[PAD]" or "[UNK]") continue;
            if (tok.StartsWith("##", StringComparison.Ordinal))
            {
                sb.Append(tok.AsSpan(2));
            }
            else
            {
                if (!first) sb.Append(' ');
                sb.Append(tok);
                first = false;
            }
        }
        return sb.ToString().Trim();
    }

    private static List<string> BasicTokenize(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new List<string>();
        var tokens = new List<string>();
        var sb = new StringBuilder();
        foreach (var ch in text)
        {
            if (char.IsWhiteSpace(ch))
            {
                Flush();
            }
            else if (IsPunctuation(ch))
            {
                Flush();
                tokens.Add(ch.ToString());
            }
            else
            {
                sb.Append(ch);
            }
        }
        Flush();
        return tokens;

        void Flush()
        {
            if (sb.Length > 0)
            {
                tokens.Add(sb.ToString());
                sb.Clear();
            }
        }
    }

    private IEnumerable<string> WordPieceTokenize(string word)
    {
        if (string.IsNullOrEmpty(word)) yield break;
        if (_vocab.ContainsKey(word)) { yield return word; yield break; }

        int start = 0;
        var chars = word;
        var pieces = new List<string>();
        while (start < chars.Length)
        {
            int end = chars.Length;
            string? cur = null;
            while (start < end)
            {
                var sub = chars.Substring(start, end - start);
                if (start > 0) sub = "##" + sub;
                if (_vocab.ContainsKey(sub)) { cur = sub; break; }
                end--;
            }
            if (cur is null)
            {
                // unknown word
                yield return _unk;
                yield break;
            }
            pieces.Add(cur);
            start = end;
        }
        foreach (var p in pieces) yield return p;
    }

    private static bool IsPunctuation(char ch)
    {
        // basic ASCII punctuation
        return char.IsPunctuation(ch) || "???????“”‘’??????{}[]()\"'.,;:!?-—/_+*=<>@#%^&`~\\|".Contains(ch);
    }
}
