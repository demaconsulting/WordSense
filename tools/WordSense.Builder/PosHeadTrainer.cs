using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     One sentence of the training or development split with the gold class of every token.
/// </summary>
/// <param name="Id">The sentence id.</param>
/// <param name="Text">The sentence text of the corpus.</param>
/// <param name="Words">The tokens in the view the library gives the encoder (see <see cref="OpenCorpus.ToTagged"/>).</param>
/// <param name="Gold">The gold class of each token (0 to 9, the parts of speech; the infinitive marker is a preposition), or -1 for punctuation and other tokens.</param>
/// <param name="Tense">The gold tense of each token that the tense head is trained on or scored with (a gold verb whose form has a past and a non-past verb analysis in the lexicon): 1 for past, 0 for non-past, -1 for every other token; null when the sentence carries no tense labels.</param>
internal sealed record PosSentence(string Id, string Text, string[] Words, int[] Gold, int[]? Tense = null);

/// <summary>
///     What identifies an encoder to the head trainer.
/// </summary>
/// <param name="ModelId">The model identifier.</param>
/// <param name="HiddenSize">The hidden size of the encoder.</param>
/// <param name="OnnxSha256">The SHA-256 of the pinned ONNX file, as lower-case hexadecimal.</param>
/// <param name="VocabularySha256">The SHA-256 of the pinned vocabulary, as lower-case hexadecimal.</param>
/// <param name="LowerCase">True when the encoder lower-cases its input.</param>
/// <param name="Runtime">The version of the inference runtime.</param>
internal sealed record PosEncoderInfo(string ModelId, int HiddenSize, string OnnxSha256, string VocabularySha256, bool LowerCase, string Runtime);

/// <summary>
///     The settings of a head training run.
/// </summary>
/// <param name="Corpus">The corpus directory.</param>
/// <param name="Data">The data directory (for the leakage check).</param>
/// <param name="TrainFile">An explicit training file, which must be the representative training split, or null.</param>
/// <param name="GoldDirectory">The directory with the treebank gold files, or null.</param>
/// <param name="ReadSet">The <c>read</c> set file, or null.</param>
/// <param name="CacheDirectory">The directory of the vector cache of this model, or null to encode without a cache.</param>
/// <param name="CGrid">The values of C to try.</param>
/// <param name="ReportFusion">Whether to report the fusion weight grid with the Basic model.</param>
/// <param name="Parallelism">The number of threads of the fit; 0 uses every core. The result does not depend on it.</param>
internal sealed record PosHeadRequest(string Corpus, string Data, string? TrainFile, string? GoldDirectory, string? ReadSet, string? CacheDirectory, IReadOnlyList<double> CGrid, bool ReportFusion, int Parallelism = 0);

/// <summary>
///     The result of a head training run.
/// </summary>
/// <param name="Blob">The bytes of the head file.</param>
/// <param name="Head">The head as the library loads it (half-precision weights).</param>
/// <param name="C">The selected regularization strength.</param>
/// <param name="Report">The report as Markdown.</param>
internal sealed record PosHeadOutcome(byte[] Blob, PosHead Head, double C, string Report);

/// <summary>
///     Measures a head on the development tokens with the restriction to the parts of speech of the lexicon.
/// </summary>
/// <param name="Scored">The scored tokens: a word of the lexicon with a gold class.</param>
/// <param name="Correct">The scored tokens the restricted head gets right.</param>
/// <param name="UnrestrictedCorrect">The scored tokens the unrestricted head gets right.</param>
/// <param name="Ambiguous">The scored tokens whose word has at least two parts of speech.</param>
/// <param name="AmbiguousCorrect">The ambiguous tokens the restricted head gets right.</param>
/// <param name="LogLoss">The mean negative log of the restricted probability of the gold class, over the scored tokens whose gold class the lexicon lists.</param>
/// <param name="Predicted">The restricted prediction of every token, or -1 for a token that is not scored.</param>
internal sealed record PosDevScore(int Scored, int Correct, int UnrestrictedCorrect, int Ambiguous, int AmbiguousCorrect, double LogLoss, int[] Predicted)
{
    /// <summary>
    ///     Gets the restricted accuracy over all scored tokens, in percent.
    /// </summary>
    public double Accuracy => Scored == 0 ? 0 : 100.0 * Correct / Scored;

    /// <summary>
    ///     Gets the restricted accuracy over the ambiguous tokens, in percent.
    /// </summary>
    public double AmbiguousAccuracy => Ambiguous == 0 ? 0 : 100.0 * AmbiguousCorrect / Ambiguous;

    /// <summary>
    ///     Gets the unrestricted accuracy over all scored tokens, in percent.
    /// </summary>
    public double UnrestrictedAccuracy => Scored == 0 ? 0 : 100.0 * UnrestrictedCorrect / Scored;
}

/// <summary>
///     Trains the part-of-speech head of an encoder model on the training split of the open corpus.
/// </summary>
/// <remarks>
///     <para>
///     The head is a multinomial logistic regression on the pooled encoder vectors of a word and its previous and next
///     word, fitted by <see cref="MultinomialLogisticTrainer"/>. The inputs are read only through
///     <see cref="OpenCorpus.ReadSplit"/> from the representative part: the training split fits the head and the
///     development split selects <c>C</c>. Nothing else is read for fitting or selection. Before it fits, the trainer
///     checks that no training sentence overlaps any held-out part (the treebank files, the <c>read</c> set, the shape
///     probe, the suite, the development and test splits and every other evaluation part) and stops with an error when
///     one does.
///     </para>
///     <para>
///     The vectors are the output of the library's own encoder method, so training and inference share the pooling. The
///     selected head must give the same dev predictions with half-precision weights as with 32-bit weights (within 0.02
///     points of accuracy), because the file stores half precision. The result is a function of the corpus, the encoder
///     files and the settings only: two runs write the same bytes.
///     </para>
/// </remarks>
internal static class PosHeadTrainer
{
    /// <summary>
    ///     The name of the feature variant that is trained: the word and its previous and next word.
    /// </summary>
    public const string Variant = "word+-1";

    /// <summary>
    ///     The values of C that are tried by default.
    /// </summary>
    public static readonly IReadOnlyList<double> DefaultCGrid = [0.001, 0.003, 0.01, 0.03, 0.1, 0.3];

    /// <summary>
    ///     The largest change of the restricted dev accuracy, in points, that rounding the weights to half precision may cause.
    /// </summary>
    public const double HalfPrecisionTolerance = 0.02;

    /// <summary>
    ///     The weights of the fusion with the Basic model that the report tries.
    /// </summary>
    public static readonly IReadOnlyList<double> FusionGrid = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1];

    /// <summary>
    ///     Reads the sentences of a split of the representative part, with the gold class of every token.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="split">The split: <c>train</c> or <c>dev</c>.</param>
    /// <returns>The sentences.</returns>
    public static List<PosSentence> ReadSentences(string corpus, string split) =>
        [.. OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, split).Select(s =>
        {
            var tagged = OpenCorpus.ToTagged(s);
            return new PosSentence(s.SentenceId, s.Text, tagged.Words, [.. tagged.Tags.Select(GoldClass)], TenseLabels(tagged));
        })];

    /// <summary>
    ///     Gets the gold tense of the tokens of a sentence that the tense head is trained on and scored with.
    /// </summary>
    /// <param name="sentence">The sentence with its gold.</param>
    /// <returns>For each token 1 (past), 0 (non-past) or -1: a token is labeled when its tag is a verb and its form has a past and a non-past verb analysis in the lexicon (<see cref="TenseEvaluator.GroupsOf"/>).</returns>
    public static int[] TenseLabels(TaggedSentence sentence)
    {
        ArgumentNullException.ThrowIfNull(sentence);
        var labels = new int[sentence.Words.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            labels[i] = -1;
            if (sentence.Gold is { } gold && sentence.Tags[i] == PosTag.Verb && TenseEvaluator.GroupsOf(sentence.Words[i]).Count > 0)
            {
                labels[i] = TenseEvaluator.IsGoldPast(gold[i]) ? 1 : 0;
            }
        }

        return labels;
    }

    /// <summary>
    ///     Trains the head.
    /// </summary>
    /// <param name="request">The settings.</param>
    /// <param name="encoder">The identity of the encoder.</param>
    /// <param name="encode">Pools the words of a sentence: one vector per word, or null for a word without word pieces.</param>
    /// <param name="progress">Receives progress lines.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The head file and the report.</returns>
    /// <exception cref="InvalidDataException">Thrown when the run must not go on: a training sentence overlaps a held-out part, the training file is not the representative training split, or the half-precision gate fails.</exception>
    public static async Task<PosHeadOutcome> TrainAsync(PosHeadRequest request, PosEncoderInfo encoder, Func<IReadOnlyList<string>, CancellationToken, ValueTask<double[]?[]>> encode, TextWriter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(progress);
        var trainFile = OpenCorpus.DataFile(request.Corpus, OpenCorpus.Representative, "train");
        if (request.TrainFile is { } given && !string.Equals(Path.GetFullPath(given), Path.GetFullPath(trainFile), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The training file must be the representative training split '{trainFile}', not '{given}'.");
        }

        var train = ReadSentences(request.Corpus, "train");
        var dev = ReadSentences(request.Corpus, "dev");
        AssertNoOverlap(request, train, progress);

        var digest = PosVectorCache.ComputeDigest(encoder.OnnxSha256, encoder.VocabularySha256, encoder.LowerCase, encoder.Runtime);
        var trainTable = await TokensAsync("train", train, encoder, encode, request.CacheDirectory, digest, progress, cancellationToken).ConfigureAwait(false);
        var devTable = await TokensAsync("dev", dev, encoder, encode, request.CacheDirectory, digest, progress, cancellationToken).ConfigureAwait(false);
        var devWords = dev.SelectMany(s => s.Words).ToArray();
        var masks = devWords.Select(LexiconMask).ToArray();
        var devVectors = ToVectors(devTable);
        var trainSha = SHA256.HashData(File.ReadAllBytes(trainFile));
        var onnx = Convert.FromHexString(encoder.OnnxSha256);

        // Fit every C, score the development split and keep the best: the restricted all-token accuracy, then the smaller C
        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"### Part-of-speech head of {encoder.ModelId}\n\n");
        report.Append(CultureInfo.InvariantCulture, $"Training: {train.Count:N0} sentences, {trainTable.TrainingRows().Length:N0} word-class tokens of the representative training split; development: {dev.Count:N0} sentences, {devTable.TrainingRows().Length:N0} tokens (selection only).\n\n");
        report.Append("| C | Iterations | Converged | Dev accuracy (restricted) | Dev ambiguous (restricted) | Dev accuracy (unrestricted) | Dev log loss |\n| ---: | ---: | --- | ---: | ---: | ---: | ---: |\n");
        PosFit? best = null;
        PosDevScore? bestScore = null;
        var bestC = 0.0;
        foreach (var c in request.CGrid.Order())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var started = DateTime.UtcNow;
            var fit = MultinomialLogisticTrainer.Fit(trainTable, new PosFitOptions(c, Parallelism: request.Parallelism));
            var head = MakeHead(encoder, onnx, trainSha, c, fit.Weights, fit.Biases);
            var score = Score(head, devVectors, devTable, masks);
            progress.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{encoder.ModelId}: C={c}: {fit.Iterations} iterations ({(fit.Converged ? "converged" : "iteration cap")}), dev {score.Accuracy:0.00}% restricted, {(DateTime.UtcNow - started).TotalSeconds:0} s."));
            report.Append(CultureInfo.InvariantCulture, $"| {c} | {fit.Iterations} | {(fit.Converged ? "yes" : "no")} | {score.Accuracy:0.00}% | {score.AmbiguousAccuracy:0.00}% | {score.UnrestrictedAccuracy:0.00}% | {score.LogLoss:0.0000} |\n");
            if (bestScore is null || Math.Round(score.Accuracy, 6) > Math.Round(bestScore.Accuracy, 6))
            {
                (best, bestScore, bestC) = (fit, score, c);
            }
        }

        var selected = best ?? throw new ArgumentException("The grid of C is empty.", nameof(request));
        report.Append(CultureInfo.InvariantCulture, $"\nSelected C = {bestC} (restricted dev accuracy {bestScore!.Accuracy:0.00}%, ambiguous {bestScore.AmbiguousAccuracy:0.00}%, {bestScore.Scored:N0} scored and {bestScore.Ambiguous:N0} ambiguous tokens).\n\n");

        // The file stores half precision: the rounded head must predict what the 32-bit head predicts
        var rounded = MakeHead(encoder, onnx, trainSha, bestC, PosHeadBlobWriter.Round(selected.Weights), selected.Biases);
        var roundedScore = Score(rounded, devVectors, devTable, masks);
        var changed = roundedScore.Predicted.Where((p, i) => p != bestScore.Predicted[i]).Count();
        report.Append(CultureInfo.InvariantCulture, $"Half precision: dev accuracy {roundedScore.Accuracy:0.00}% against {bestScore.Accuracy:0.00}% with 32-bit weights ({changed} predictions differ); the gate is {HalfPrecisionTolerance:0.00} points.\n");
        CheckHalfPrecision(bestScore.Accuracy, roundedScore.Accuracy);

        // The file must read back as the rounded head and predict the same
        var blob = PosHeadBlobWriter.Write(rounded.Data);
        var reread = new PosHead(PosHeadBlob.Read(blob));
        var rereadScore = Score(reread, devVectors, devTable, masks);
        if (!rereadScore.Predicted.AsSpan().SequenceEqual(roundedScore.Predicted))
        {
            throw new InvalidDataException("The head file does not read back to the head that was trained.");
        }

        report.Append(CultureInfo.InvariantCulture, $"Head file: {blob.Length:N0} bytes, training file SHA-256 {Convert.ToHexString(trainSha).ToLowerInvariant()}.\n");
        if (request.ReportFusion)
        {
            report.Append('\n').Append(await FusionReportAsync(rounded, devVectors, devTable, dev, masks, cancellationToken).ConfigureAwait(false));
        }

        return new PosHeadOutcome(blob, new PosHead(PosHeadBlob.Read(blob)), bestC, report.ToString());
    }

    /// <summary>
    ///     Checks that rounding the weights to half precision leaves the restricted dev accuracy within the gate.
    /// </summary>
    /// <param name="fullAccuracy">The restricted dev accuracy with 32-bit weights, in percent.</param>
    /// <param name="halfAccuracy">The restricted dev accuracy with half-precision weights, in percent.</param>
    /// <exception cref="InvalidDataException">Thrown when the accuracies differ by more than <see cref="HalfPrecisionTolerance"/> points.</exception>
    public static void CheckHalfPrecision(double fullAccuracy, double halfAccuracy)
    {
        var difference = Math.Abs(halfAccuracy - fullAccuracy);
        if (difference > HalfPrecisionTolerance)
        {
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture, $"The half-precision head changes the dev accuracy by {difference:0.000} points, more than {HalfPrecisionTolerance:0.00}."));
        }
    }

    /// <summary>
    ///     Measures a head on a table of development tokens.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="vectors">The vectors of the tokens (null for a token without word pieces).</param>
    /// <param name="table">The tokens.</param>
    /// <param name="masks">The parts of speech the lexicon lists for each token, as a bit set.</param>
    /// <returns>The score.</returns>
    public static PosDevScore Score(PosHead head, double[]?[] vectors, PosTokenTable table, int[] masks)
    {
        ArgumentNullException.ThrowIfNull(head);
        var posteriors = Posteriors(head, vectors, table);
        return Score(posteriors, table, masks);
    }

    /// <summary>
    ///     Scores posteriors with the restriction to the parts of speech of the lexicon.
    /// </summary>
    /// <param name="posteriors">The unrestricted posterior of every token.</param>
    /// <param name="table">The tokens.</param>
    /// <param name="masks">The parts of speech the lexicon lists for each token, as a bit set.</param>
    /// <returns>The score.</returns>
    public static PosDevScore Score(double[][] posteriors, PosTokenTable table, int[] masks)
    {
        ArgumentNullException.ThrowIfNull(posteriors);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(masks);
        int scored = 0, correct = 0, unrestrictedCorrect = 0, ambiguous = 0, ambiguousCorrect = 0, lossTokens = 0;
        var loss = 0.0;
        var predicted = new int[table.Count];
        Array.Fill(predicted, -1);
        for (var t = 0; t < table.Count; t++)
        {
            if (table.Gold[t] < 0 || masks[t] == 0)
            {
                continue;
            }

            var restricted = PosHead.Restrict(posteriors[t], masks[t]);
            var best = ArgMax(restricted);
            predicted[t] = best;
            scored++;
            correct += best == table.Gold[t] ? 1 : 0;
            unrestrictedCorrect += ArgMax(posteriors[t]) == table.Gold[t] ? 1 : 0;
            if (int.PopCount(masks[t]) >= 2)
            {
                ambiguous++;
                ambiguousCorrect += best == table.Gold[t] ? 1 : 0;
            }

            if ((masks[t] & (1 << table.Gold[t])) != 0)
            {
                loss -= Math.Log(Math.Max(restricted[table.Gold[t]], 1e-12));
                lossTokens++;
            }
        }

        return new PosDevScore(scored, correct, unrestrictedCorrect, ambiguous, ambiguousCorrect, lossTokens == 0 ? 0 : loss / lossTokens, predicted);
    }

    /// <summary>
    ///     Computes the unrestricted posterior of every token.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="vectors">The vectors of the tokens.</param>
    /// <param name="table">The tokens.</param>
    /// <returns>One posterior per token.</returns>
    public static double[][] Posteriors(PosHead head, double[]?[] vectors, PosTokenTable table)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(vectors);
        ArgumentNullException.ThrowIfNull(table);
        var result = new double[table.Count][];
        Parallel.For(0, table.Count, t => result[t] = head.Posterior(table.HasPrevious(t) ? vectors[t - 1] : null, vectors[t], table.HasNext(t) ? vectors[t + 1] : null));
        return result;
    }

    /// <summary>
    ///     Gets the parts of speech that the lexicon lists for a written word.
    /// </summary>
    /// <param name="word">The word as written.</param>
    /// <returns>The parts of speech as a bit set (bit <c>(int)PartOfSpeech</c>), or 0 for a word that is not in the lexicon.</returns>
    public static int LexiconMask(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        var key = BasicClassifier.Normalize(word);
        if (key.Length == 0 || !WordLexicon.TryLookup(key, out var analyses))
        {
            return 0;
        }

        var mask = 0;
        foreach (var analysis in analyses)
        {
            mask |= 1 << (int)analysis.PartOfSpeech;
        }

        return mask;
    }

    /// <summary>
    ///     Converts the vectors of a table to the 64-bit vectors the head reads.
    /// </summary>
    /// <param name="table">The tokens.</param>
    /// <returns>One vector per token, or null for a token without a vector.</returns>
    public static double[]?[] ToVectors(PosTokenTable table)
    {
        ArgumentNullException.ThrowIfNull(table);
        var d = table.Dimension;
        var result = new double[]?[table.Count];
        for (var t = 0; t < table.Count; t++)
        {
            if (!table.HasVector[t])
            {
                continue;
            }

            var v = new double[d];
            for (var j = 0; j < d; j++)
            {
                v[j] = table.Vectors[(t * d) + j];
            }

            result[t] = v;
        }

        return result;
    }

    private static int GoldClass(int tag) => tag switch
    {
        PosTag.InfinitiveTo => PosTag.Preposition,
        >= PosTag.Punctuation or < 0 => -1,
        _ => tag,
    };

    private static PosHead MakeHead(PosEncoderInfo encoder, byte[] onnx, byte[] trainSha, double c, float[] weights, float[] biases) =>
        new(new PosHeadData(encoder.ModelId, encoder.HiddenSize, onnx, PosHeadBlob.WordAndNeighbors, PosHeadBlob.ExpectedClassNames(), trainSha, (float)c, Variant, weights, biases));

    /// <summary>
    ///     Aborts when a training sentence overlaps a held-out part.
    /// </summary>
    internal static void AssertNoOverlap(PosHeadRequest request, List<PosSentence> train, TextWriter progress)
    {
        var heldOut = LeakageCheck.HeldOutSets(request.Corpus, request.Data, request.GoldDirectory, request.ReadSet);
        var overlaps = LeakageCheck.FindTrainingOverlaps([.. train.Select(s => (s.Id, s.Text))], heldOut);
        progress.WriteLine($"Leakage check: {train.Count:N0} training sentences against {heldOut.Count} held-out parts ({string.Join(", ", heldOut.Select(h => h.Name))}): {overlaps.Count} overlaps.");
        if (overlaps.Count > 0)
        {
            var examples = string.Join("; ", overlaps.Take(5).Select(o => $"{o.Part}: {o.TrainSource} / {o.HeldOutSource} ({o.Kind})"));
            throw new InvalidDataException($"The training split overlaps a held-out part in {overlaps.Count} sentences, so no head is trained: {examples}.");
        }
    }

    /// <summary>
    ///     Gets the vectors of a set, from the cache when it matches, else by encoding.
    /// </summary>
    internal static async Task<PosTokenTable> TokensAsync(string name, List<PosSentence> sentences, PosEncoderInfo encoder, Func<IReadOnlyList<string>, CancellationToken, ValueTask<double[]?[]>> encode, string? cacheDirectory, byte[] digest, TextWriter progress, CancellationToken cancellationToken)
    {
        var count = sentences.Sum(s => s.Words.Length);
        var d = encoder.HiddenSize;
        var sentence = new int[count];
        var gold = new int[count];
        var at = 0;
        for (var i = 0; i < sentences.Count; i++)
        {
            for (var j = 0; j < sentences[i].Words.Length; j++)
            {
                sentence[at] = i;
                gold[at] = sentences[i].Gold[j];
                at++;
            }
        }

        var wordHash = PosVectorCache.HashWords(sentences.Select(s => s.Words));
        var path = cacheDirectory is null ? null : Path.Join(cacheDirectory, name + ".bin");
        if (path is not null && PosVectorCache.TryRead(path, digest, wordHash, d, count) is { } cached)
        {
            progress.WriteLine($"{encoder.ModelId}: {name}: {count:N0} token vectors from the cache.");
            return new PosTokenTable(d, cached.Vectors, sentence, gold, cached.HasVector);
        }

        var vectors = new float[count * d];
        var has = new bool[count];
        var started = DateTime.UtcNow;
        at = 0;
        for (var i = 0; i < sentences.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var pooled = await encode(sentences[i].Words, cancellationToken).ConfigureAwait(false);
            for (var j = 0; j < pooled.Length; j++)
            {
                if (pooled[j] is { } v)
                {
                    has[at] = true;
                    for (var k = 0; k < d; k++)
                    {
                        vectors[(at * d) + k] = (float)v[k];
                    }
                }

                at++;
            }

            if ((i + 1) % 2000 == 0)
            {
                progress.WriteLine($"{encoder.ModelId}: {name}: {i + 1:N0}/{sentences.Count:N0} sentences encoded ({(DateTime.UtcNow - started).TotalSeconds:0} s).");
            }
        }

        var table = new PosTokenTable(d, vectors, sentence, gold, has);
        if (path is not null)
        {
            PosVectorCache.Write(path, digest, wordHash, table);
        }

        progress.WriteLine($"{encoder.ModelId}: {name}: {count:N0} tokens of {sentences.Count:N0} sentences encoded in {(DateTime.UtcNow - started).TotalSeconds:0} s.");
        return table;
    }

    /// <summary>
    ///     Reports the dev accuracy and log loss of the head fused with the Basic model at each weight.
    /// </summary>
    private static async Task<string> FusionReportAsync(PosHead head, double[]?[] vectors, PosTokenTable table, List<PosSentence> dev, int[] masks, CancellationToken cancellationToken)
    {
        var posteriors = Posteriors(head, vectors, table);
        var basic = new double[table.Count][];
        using var model = new BasicWordSenseModel();
        var at = 0;
        foreach (var sentence in dev)
        {
            var results = await model.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
            foreach (var result in results)
            {
                var p = new double[MultinomialLogisticTrainer.Classes];
                foreach (var analysis in result.Analyses)
                {
                    p[(int)analysis.Details.PartOfSpeech] += analysis.Probability;
                }

                basic[at++] = p;
            }
        }

        var text = new StringBuilder();
        text.Append("Fusion with the Basic model (the head to the power w times the Basic probabilities to the power 1 - w, restricted):\n\n");
        text.Append("| w | Dev accuracy | Dev ambiguous | Dev log loss |\n| ---: | ---: | ---: | ---: |\n");
        foreach (var w in FusionGrid)
        {
            var fused = new double[table.Count][];
            for (var t = 0; t < table.Count; t++)
            {
                var f = new double[MultinomialLogisticTrainer.Classes];
                var total = 0.0;
                for (var c = 0; c < f.Length; c++)
                {
                    f[c] = Math.Pow(Math.Max(posteriors[t][c], 1e-9), w) * Math.Pow(basic[t][c] + 1e-4, 1 - w);
                    total += f[c];
                }

                for (var c = 0; c < f.Length; c++)
                {
                    f[c] /= total;
                }

                fused[t] = f;
            }

            var score = Score(fused, table, masks);
            text.Append(CultureInfo.InvariantCulture, $"| {w:0.0} | {score.Accuracy:0.00}% | {score.AmbiguousAccuracy:0.00}% | {score.LogLoss:0.0000} |\n");
        }

        return text.ToString();
    }

    private static int ArgMax(double[] values)
    {
        var best = 0;
        for (var i = 1; i < values.Length; i++)
        {
            if (values[i] > values[best])
            {
                best = i;
            }
        }

        return best;
    }
}
