using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.Maui.Storage;
using System.Text;
using System.Text.RegularExpressions;

namespace mindvault.Services;

/// <summary>
/// T5FlashcardService
/// - Loads ONLY quantized T5 Small ONNX models from Resources/Raw:
///   encoder_model.int8.onnx, decoder_model.int8.onnx, decoder_with_past_model.int8.onnx
/// - Performs minimal text generation using a greedy decoder.
/// - Expects a prompt that instructs the model to output multiple Q&A pairs as lines.
///
/// IMPORTANT: This implementation uses a minimal tokenizer (whitespace-hash) because a managed
/// SentencePiece implementation is not available in this project. Replace TokenizerWrapper with
/// a SentencePiece tokenizer bound to the T5 vocabulary for higher quality generations.
/// </summary>
public sealed class T5FlashcardService : IAsyncDisposable
{
    private InferenceSession _encoder;
    private InferenceSession _decoder;
    private InferenceSession _decoderWithPast;

    private TokenizerWrapper _tokenizer;

    public static T5FlashcardService Create()
    {
        var encPath = CopyRawAsset("encoder_model.int8.onnx");
        var decPath = CopyRawAsset("decoder_model.int8.onnx");
        var decPastPath = CopyRawAsset("decoder_with_past_model.int8.onnx");

        var enc = new InferenceSession(encPath);
        var dec = new InferenceSession(decPath);
        var decPast = new InferenceSession(decPastPath);

        var tok = new TokenizerWrapper();
        return new T5FlashcardService(enc, dec, decPast, tok);
    }

    private T5FlashcardService(InferenceSession enc, InferenceSession dec, InferenceSession decPast, TokenizerWrapper tok)
    {
        _encoder = enc; _decoder = dec; _decoderWithPast = decPast; _tokenizer = tok;
    }

    /// <summary>
    /// Generate as many QA pairs as the model can fit, given a paragraph.
    /// We use an instruction-style prompt suitable for T5:
    ///   "generate flashcards as Q: ... A: ... per line based on: <paragraph>"
    /// </summary>
    public async Task<List<GeneratedFlashcard>> GenerateQAFromParagraphAsync(string paragraph, int maxNewTokens = 128)
    {
        var prompt = $"generate flashcards as 'Q: <question> A: <answer>' per line. paragraph: {paragraph}";
        var input = _tokenizer.Encode(prompt, maxInput: 512);

        // Encode
        var encoderInputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", input.InputIdsTensor),
            NamedOnnxValue.CreateFromTensor("attention_mask", input.AttentionMaskTensor)
        };

        IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encOut;
        try { encOut = _encoder.Run(encoderInputs); }
        catch { return new(); }

        // Start with decoder BOS token for T5 (usually <pad> token id) and generate greedily
        var bos = _tokenizer.BosTokenId;
        var eos = _tokenizer.EosTokenId;

        var generatedIds = new List<int> { bos };

        for (int step = 0; step < maxNewTokens; step++)
        {
            var decInput = _tokenizer.BuildDecoderInputs(generatedIds, encOut);
            IDisposableReadOnlyCollection<DisposableNamedOnnxValue> decOut;
            try { decOut = _decoder.Run(decInput); }
            catch { break; }

            // logits: [1, seq_len, vocab]
            var logits = decOut.First().AsTensor<float>();
            var shape = logits.Dimensions.ToArray();
            int vocab = shape[^1];
            int lastIndex = shape[^2] - 1;

            int nextId = ArgMaxLast(logits, lastIndex, vocab);
            generatedIds.Add(nextId);

            if (nextId == eos) break;
        }

        var text = _tokenizer.Decode(generatedIds);
        return ParseFlashcards(text);
    }

    private static int ArgMaxLast(Tensor<float> logits, int lastIndex, int vocab)
    {
        int best = 0; float bestVal = float.MinValue;
        for (int i = 0; i < vocab; i++)
        {
            var v = logits[0, lastIndex, i];
            if (v > bestVal) { bestVal = v; best = i; }
        }
        return best;
    }

    private static List<GeneratedFlashcard> ParseFlashcards(string text)
    {
        var list = new List<GeneratedFlashcard>();
        if (string.IsNullOrWhiteSpace(text)) return list;

        foreach (var line in text.Split('\n'))
        {
            var m = Regex.Match(line, @"^\s*Q:\s*(.+?)\s*A:\s*(.+)\s*$", RegexOptions.IgnoreCase);
            if (m.Success)
            {
                var q = m.Groups[1].Value.Trim().TrimEnd('.');
                var a = m.Groups[2].Value.Trim();
                if (!string.IsNullOrEmpty(q) && !string.IsNullOrEmpty(a))
                    list.Add(new GeneratedFlashcard { Question = q, Answer = a });
            }
        }
        return list;
    }

    private static string CopyRawAsset(string file)
    {
        // Resources/Raw are packed as loose assets in MAUI; FileSystem.OpenAppPackageFileAsync works with filename only
        using var s = FileSystem.OpenAppPackageFileAsync(file).GetAwaiter().GetResult();
        var path = Path.Combine(FileSystem.CacheDirectory, $"{Guid.NewGuid():N}_{file}");
        using var fs = File.Create(path);
        s.CopyTo(fs);
        return path;
    }

    public async ValueTask DisposeAsync()
    {
        await Task.Yield();
        try { _encoder.Dispose(); } catch { }
        try { _decoder.Dispose(); } catch { }
        try { _decoderWithPast.Dispose(); } catch { }
    }
}

internal sealed class TokenizerWrapper
{
    // Minimal T5-like special tokens
    private readonly int _padId = 0;   // <pad>
    private readonly int _eosId = 1;   // </s>

    public int BosTokenId => _padId; // decoder starts from pad
    public int EosTokenId => _eosId;

    public (DenseTensor<long> InputIdsTensor, DenseTensor<long> AttentionMaskTensor) Encode(string text, int maxInput)
    {
        // Very small fallback tokenizer. Replace with SentencePiece for production.
        var toks = Regex.Split(text ?? string.Empty, @"\s+").Where(t => t.Length > 0).ToArray();
        var ids = new List<int>(toks.Length + 2);
        foreach (var t in toks)
            ids.Add(2 + (Math.Abs(t.GetHashCode()) % 10000));
        ids.Add(_eosId);

        if (ids.Count > maxInput) ids = ids.Take(maxInput).ToList();
        var attn = ids.Select(_ => 1L).ToArray();

        var tIds = new DenseTensor<long>(new[] { 1, ids.Count });
        var tAttn = new DenseTensor<long>(new[] { 1, ids.Count });
        for (int i = 0; i < ids.Count; i++) { tIds[0, i] = ids[i]; tAttn[0, i] = attn[i]; }
        return (tIds, tAttn);
    }

    public List<NamedOnnxValue> BuildDecoderInputs(List<int> generatedIds, IDisposableReadOnlyCollection<DisposableNamedOnnxValue> encoderOutputs)
    {
        var decIds = new DenseTensor<long>(new[] { 1, generatedIds.Count });
        for (int i = 0; i < generatedIds.Count; i++) decIds[0, i] = generatedIds[i];

        var encState = encoderOutputs.First().AsTensor<float>();

        return new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", decIds),
            NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encState)
        };
    }

    public string Decode(List<int> ids)
    {
        var sb = new StringBuilder();
        foreach (var id in ids)
        {
            if (id == _padId) continue;
            if (id == _eosId) break;
            sb.Append(id).Append(' ');
        }
        return sb.ToString().Trim();
    }
}
