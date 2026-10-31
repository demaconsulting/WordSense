using System.Globalization;
using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Onnx;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The training and development rows of the tense head: the tokens that are gold verbs whose form has a past and a
///     non-past verb analysis, with their gold class.
/// </summary>
/// <param name="Tokens">The index in the token table of each row.</param>
/// <param name="Past">Whether the gold class of each row is past.</param>
/// <param name="Forms">The form (lower case) of each row.</param>
internal sealed record TenseRows(int[] Tokens, bool[] Past, string[] Forms)
{
    /// <summary>
    ///     Gets the number of rows.
    /// </summary>
    public int Count => Tokens.Length;

    /// <summary>
    ///     Gets the number of past rows.
    /// </summary>
    public int PastCount => Past.Count(p => p);
}

/// <summary>
///     A fitted tense head.
/// </summary>
/// <param name="Weights">The weights with the standardization folded in, for the raw pooled vectors.</param>
/// <param name="Bias">The bias with the standardization folded in.</param>
/// <param name="Iterations">The number of iterations.</param>
/// <param name="Converged">True when the fit reached the tolerance.</param>
internal sealed record TenseFit(float[] Weights, float Bias, int Iterations, bool Converged);

/// <summary>
///     The score of a tense head on the development rows.
/// </summary>
/// <param name="Rows">The number of rows.</param>
/// <param name="Correct">The rows whose class the head gets right (past when the probability is above one half).</param>
/// <param name="LogLoss">The mean negative log of the probability of the gold class.</param>
/// <param name="Probability">The probability of past of each row.</param>
internal sealed record TenseDevScore(int Rows, int Correct, double LogLoss, double[] Probability)
{
    /// <summary>
    ///     Gets the accuracy in percent.
    /// </summary>
    public double Accuracy => Rows == 0 ? 0 : 100.0 * Correct / Rows;
}

/// <summary>
///     The settings of a tense head training run.
/// </summary>
/// <param name="Corpus">The corpus directory.</param>
/// <param name="Data">The data directory (for the leakage check).</param>
/// <param name="GoldDirectory">The directory with the treebank gold files, or null.</param>
/// <param name="ReadSet">The <c>read</c> set file, or null.</param>
/// <param name="CacheDirectory">The directory of the vector cache of the model, or null to encode without a cache.</param>
/// <param name="CGrid">The values of C to try.</param>
/// <param name="ReportFusion">Whether to report the combination with the Basic model at each weight.</param>
internal sealed record TenseHeadRequest(string Corpus, string Data, string? GoldDirectory, string? ReadSet, string? CacheDirectory, IReadOnlyList<double> CGrid, bool ReportFusion);

/// <summary>
///     The result of a tense head training run.
/// </summary>
/// <param name="Blob">The bytes of the head file, with the tense section.</param>
/// <param name="Head">The tense head as the library loads it (half-precision weights).</param>
/// <param name="Report">The report as Markdown.</param>
internal sealed record TenseHeadOutcome(byte[] Blob, TenseHead Head, string Report);

/// <summary>
///     Trains the tense head of an encoder model on the training split of the open corpus and adds it to the head file.
/// </summary>
/// <remarks>
///     <para>
///     The head is a binary logistic regression on the pooled encoder vector of a verb whose form has a past and a non-past
///     verb analysis in the lexicon (a same-spelling verb: <c>read</c>, <c>put</c>, <c>found</c>), fitted by
///     <see cref="Lbfgs"/> with the same scaling of C as <see cref="MultinomialLogisticTrainer"/>. The inputs are read only
///     through <see cref="OpenCorpus.ReadSplit"/> from the representative part: the training split fits the head and the
///     development split selects the feature layout (the word, or the word and its neighbors) and C, by the mean log
///     loss of the development rows. Nothing else is read. Before it fits, the trainer checks that no training sentence
///     overlaps any held-out part and stops with an error when one does.
///     </para>
///     <para>
///     The part-of-speech section of the existing head file is kept byte for byte: only the tense section is replaced.
///     The training file of that head must be the file this run fits on. The selected head must predict the same on the
///     development rows with half-precision weights as with 32-bit weights, because the file stores half precision. The
///     result is a function of the corpus, the encoder files and the settings only: two runs write the same bytes.
///     </para>
/// </remarks>
internal static class TenseHeadTrainer
{
    /// <summary>
    ///     The values of C that are tried by default.
    /// </summary>
    public static readonly IReadOnlyList<double> DefaultCGrid = [0.0001, 0.0003, 0.001, 0.003, 0.01, 0.03, 0.1, 0.3];

    /// <summary>
    ///     The feature layouts that are tried, the simplest first.
    /// </summary>
    public static readonly IReadOnlyList<int> Layouts = [PosHeadBlob.TenseWord, PosHeadBlob.TenseWordAndNeighbors];

    /// <summary>
    ///     The weights of the combination with the Basic model that the report tries.
    /// </summary>
    public static readonly IReadOnlyList<double> FusionGrid = [0, 0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8, 0.9, 1];

    /// <summary>
    ///     The largest gradient component at which the fit counts as converged; the same scale as the other fits of the
    ///     builder, on the standardized features.
    /// </summary>
    private const double GradientTolerance = 1e-5;

    /// <summary>
    ///     The number of iterations after which the fit stops without having converged, so that a run always ends; the
    ///     report says when the cap was reached.
    /// </summary>
    private const int MaxIterations = 500;

    /// <summary>
    ///     Finds the rows of the tense head in a table of tokens.
    /// </summary>
    /// <param name="table">The tokens.</param>
    /// <param name="sentences">The sentences of the table, in order, with their tense labels.</param>
    /// <returns>The tokens with a vector and a tense label, in token order.</returns>
    public static TenseRows Rows(PosTokenTable table, IReadOnlyList<PosSentence> sentences)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(sentences);
        var tokens = new List<int>();
        var past = new List<bool>();
        var forms = new List<string>();
        var at = 0;
        foreach (var sentence in sentences)
        {
            for (var j = 0; j < sentence.Words.Length; j++, at++)
            {
                if (sentence.Tense is { } tense && tense[j] >= 0 && table.HasVector[at])
                {
                    tokens.Add(at);
                    past.Add(tense[j] == 1);
                    forms.Add(sentence.Words[j].ToLowerInvariant());
                }
            }
        }

        return new TenseRows([.. tokens], [.. past], [.. forms]);
    }

    /// <summary>
    ///     Fits the head.
    /// </summary>
    /// <param name="table">The training tokens.</param>
    /// <param name="rows">The training rows.</param>
    /// <param name="layout">The feature layout, <see cref="PosHeadBlob.TenseWord"/> or <see cref="PosHeadBlob.TenseWordAndNeighbors"/>.</param>
    /// <param name="c">The inverse regularization strength: the objective is the mean loss plus <c>‖w‖² / (2 C n)</c>.</param>
    /// <returns>The fitted head.</returns>
    /// <exception cref="ArgumentException">Thrown when there is no training row, the layout is unknown or C is not a positive number.</exception>
    public static TenseFit Fit(PosTokenTable table, TenseRows rows, int layout, double c)
    {
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
        {
            throw new ArgumentException("There is no tense training row.", nameof(rows));
        }

        if (!(c > 0) || !double.IsFinite(c))
        {
            throw new ArgumentException("The regularization strength C must be a positive number.", nameof(c));
        }

        var features = FeatureCount(layout, table.Dimension);
        var x = new double[rows.Count][];
        for (var r = 0; r < x.Length; r++)
        {
            x[r] = Features(table, rows.Tokens[r], layout);
        }

        var n = x.Length;
        var mean = new double[features];
        var std = new double[features];
        foreach (var row in x)
        {
            for (var q = 0; q < features; q++)
            {
                mean[q] += row[q];
            }
        }

        for (var q = 0; q < features; q++)
        {
            mean[q] /= n;
        }

        foreach (var row in x)
        {
            for (var q = 0; q < features; q++)
            {
                var d = row[q] - mean[q];
                std[q] += d * d;
            }
        }

        for (var q = 0; q < features; q++)
        {
            std[q] = Math.Sqrt(std[q] / n) + 1e-6;
            for (var r = 0; r < n; r++)
            {
                x[r][q] = (x[r][q] - mean[q]) / std[q];
            }
        }

        var scale = 1.0 / (c * n);
        var objective = (double[] theta, double[] gradient) =>
        {
            Array.Clear(gradient);
            var loss = 0.0;
            for (var r = 0; r < n; r++)
            {
                var z = theta[features];
                var row = x[r];
                for (var q = 0; q < features; q++)
                {
                    z += theta[q] * row[q];
                }

                var y = rows.Past[r] ? 1.0 : 0.0;
                loss += (z > 0 ? z + Math.Log(1.0 + Math.Exp(-z)) : Math.Log(1.0 + Math.Exp(z))) - (y * z);
                var g = ((1.0 / (1.0 + Math.Exp(-z))) - y) / n;
                for (var q = 0; q < features; q++)
                {
                    gradient[q] += g * row[q];
                }

                gradient[features] += g;
            }

            var penalty = 0.0;
            for (var q = 0; q < features; q++)
            {
                gradient[q] += theta[q] * scale;
                penalty += theta[q] * theta[q];
            }

            return (loss / n) + (0.5 * scale * penalty);
        };
        var result = Lbfgs.Minimize(objective, new double[features + 1], GradientTolerance, MaxIterations);

        // Fold the standardization into the weights and the bias
        var weights = new float[features];
        var bias = result.X[features];
        for (var q = 0; q < features; q++)
        {
            weights[q] = (float)(result.X[q] / std[q]);
            bias -= result.X[q] * mean[q] / std[q];
        }

        return new TenseFit(weights, (float)bias, result.Iterations, result.Converged);
    }

    /// <summary>
    ///     Scores a head on rows.
    /// </summary>
    /// <param name="head">The head.</param>
    /// <param name="table">The tokens.</param>
    /// <param name="rows">The rows.</param>
    /// <returns>The score.</returns>
    public static TenseDevScore Score(TenseHead head, PosTokenTable table, TenseRows rows)
    {
        ArgumentNullException.ThrowIfNull(head);
        ArgumentNullException.ThrowIfNull(table);
        ArgumentNullException.ThrowIfNull(rows);
        var probability = new double[rows.Count];
        var correct = 0;
        var loss = 0.0;
        for (var r = 0; r < rows.Count; r++)
        {
            var t = rows.Tokens[r];
            var p = head.PastProbability(table.HasPrevious(t) ? Vector(table, t - 1) : null, Vector(table, t)!, table.HasNext(t) ? Vector(table, t + 1) : null);
            probability[r] = p;
            correct += (p > 0.5) == rows.Past[r] ? 1 : 0;
            loss -= Math.Log(Math.Max(rows.Past[r] ? p : 1.0 - p, 1e-12));
        }

        return new TenseDevScore(rows.Count, correct, rows.Count == 0 ? 0 : loss / rows.Count, probability);
    }

    /// <summary>
    ///     Trains the tense head and adds it to a head file.
    /// </summary>
    /// <param name="request">The settings.</param>
    /// <param name="encoder">The identity of the encoder.</param>
    /// <param name="existing">The head file as it is: its part-of-speech section is kept.</param>
    /// <param name="encode">Pools the words of a sentence: one vector per word, or null for a word without word pieces.</param>
    /// <param name="progress">Receives progress lines.</param>
    /// <param name="cancellationToken">A token that cancels the run.</param>
    /// <returns>The head file with the tense section and the report.</returns>
    /// <exception cref="InvalidDataException">Thrown when the run must not go on: the existing head does not belong to this encoder or was not trained on the training split, a training sentence overlaps a held-out part, there are no development rows or the half-precision gate fails.</exception>
    public static async Task<TenseHeadOutcome> TrainAsync(TenseHeadRequest request, PosEncoderInfo encoder, PosHeadData existing, Func<IReadOnlyList<string>, CancellationToken, ValueTask<double[]?[]>> encode, TextWriter progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(existing);
        ArgumentNullException.ThrowIfNull(encode);
        ArgumentNullException.ThrowIfNull(progress);
        var trainFile = OpenCorpus.DataFile(request.Corpus, OpenCorpus.Representative, "train");
        var trainSha = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(trainFile));
        if (existing.ModelId != encoder.ModelId || existing.HiddenSize != encoder.HiddenSize || !existing.EncoderSha256.AsSpan().SequenceEqual(Convert.FromHexString(encoder.OnnxSha256)))
        {
            throw new InvalidDataException("The existing head file does not belong to this encoder.");
        }

        if (!existing.TrainSha256.AsSpan().SequenceEqual(trainSha))
        {
            throw new InvalidDataException("The existing head file was not trained on the representative training split of this corpus, so a tense head is not added to it.");
        }

        var train = PosHeadTrainer.ReadSentences(request.Corpus, "train");
        var dev = PosHeadTrainer.ReadSentences(request.Corpus, "dev");
        PosHeadTrainer.AssertNoOverlap(new PosHeadRequest(request.Corpus, request.Data, null, request.GoldDirectory, request.ReadSet, request.CacheDirectory, request.CGrid, false), train, progress);
        var digest = PosVectorCache.ComputeDigest(encoder.OnnxSha256, encoder.VocabularySha256, encoder.LowerCase, encoder.Runtime);
        var trainTable = await PosHeadTrainer.TokensAsync("train", train, encoder, encode, request.CacheDirectory, digest, progress, cancellationToken).ConfigureAwait(false);
        var devTable = await PosHeadTrainer.TokensAsync("dev", dev, encoder, encode, request.CacheDirectory, digest, progress, cancellationToken).ConfigureAwait(false);
        var trainRows = Rows(trainTable, train);
        var devRows = Rows(devTable, dev);
        if (trainRows.Count == 0 || devRows.Count == 0)
        {
            throw new InvalidDataException($"The tense head needs training and development rows, and there are {trainRows.Count} and {devRows.Count}.");
        }

        var report = new StringBuilder();
        report.Append(CultureInfo.InvariantCulture, $"### Tense head of {encoder.ModelId}\n\n");
        report.Append(CultureInfo.InvariantCulture, $"Training: {trainRows.Count:N0} same-spelling verb tokens ({trainRows.PastCount:N0} past, {trainRows.Forms.Distinct().Count():N0} forms) of the representative training split; development: {devRows.Count:N0} tokens ({devRows.PastCount:N0} past, {devRows.Forms.Distinct().Count():N0} forms, selection only). Majority class on development: {100.0 * Math.Max(devRows.PastCount, devRows.Count - devRows.PastCount) / devRows.Count:0.0}%.\n\n");
        report.Append("| Layout | C | Iterations | Converged | Dev accuracy | Dev log loss |\n| --- | ---: | ---: | --- | ---: | ---: |\n");
        TenseFit? best = null;
        TenseDevScore? bestScore = null;
        (int Layout, double C) bestSetting = default;
        foreach (var layout in Layouts)
        {
            foreach (var c in request.CGrid.Order())
            {
                cancellationToken.ThrowIfCancellationRequested();
                var fit = Fit(trainTable, trainRows, layout, c);
                var score = Score(Make(layout, c, fit.Weights, fit.Bias, existing.HiddenSize), devTable, devRows);
                progress.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{encoder.ModelId}: tense layout {layout} C={c}: {fit.Iterations} iterations ({(fit.Converged ? "converged" : "iteration cap")}), dev {score.Accuracy:0.00}%, log loss {score.LogLoss:0.0000}."));
                report.Append(CultureInfo.InvariantCulture, $"| {LayoutName(layout)} | {c} | {fit.Iterations} | {(fit.Converged ? "yes" : "no")} | {score.Accuracy:0.00}% ({score.Correct}/{score.Rows}) | {score.LogLoss:0.0000} |\n");

                // The smallest log loss; a tie keeps the simpler layout and the smaller C (the first one tried)
                if (bestScore is null || Math.Round(score.LogLoss, 9) < Math.Round(bestScore.LogLoss, 9))
                {
                    (best, bestScore, bestSetting) = (fit, score, (layout, c));
                }
            }
        }

        var selected = best ?? throw new ArgumentException("The grid of C is empty.", nameof(request));
        report.Append(CultureInfo.InvariantCulture, $"\nSelected: {LayoutName(bestSetting.Layout)}, C = {bestSetting.C} (dev accuracy {bestScore!.Accuracy:0.00}%, log loss {bestScore.LogLoss:0.0000}).\n\n");

        // The file stores half precision: the rounded head must predict what the 32-bit head predicts
        var roundedWeights = PosHeadBlobWriter.Round(selected.Weights);
        var rounded = Make(bestSetting.Layout, bestSetting.C, roundedWeights, selected.Bias, existing.HiddenSize);
        var roundedScore = Score(rounded, devTable, devRows);
        var changed = Enumerable.Range(0, devRows.Count).Count(r => (roundedScore.Probability[r] > 0.5) != (bestScore.Probability[r] > 0.5));
        report.Append(CultureInfo.InvariantCulture, $"Half precision: dev accuracy {roundedScore.Accuracy:0.00}% against {bestScore.Accuracy:0.00}% with 32-bit weights ({changed} predictions differ); the gate is {PosHeadTrainer.HalfPrecisionTolerance:0.00} points.\n");
        PosHeadTrainer.CheckHalfPrecision(bestScore.Accuracy, roundedScore.Accuracy);

        // The file must read back as the rounded head and predict the same
        var blob = PosHeadBlobWriter.Write(existing with { Tense = rounded.Data });
        var reread = new PosHead(PosHeadBlob.Read(blob));
        var rereadScore = Score(reread.Tense!, devTable, devRows);
        if (!rereadScore.Probability.AsSpan().SequenceEqual(roundedScore.Probability))
        {
            throw new InvalidDataException("The head file does not read back to the tense head that was trained.");
        }

        report.Append(CultureInfo.InvariantCulture, $"Head file: {blob.Length:N0} bytes, training file SHA-256 {Convert.ToHexString(trainSha).ToLowerInvariant()}.\n");
        if (request.ReportFusion)
        {
            report.Append('\n').Append(await FusionReportAsync(reread.Tense!, devTable, devRows, dev, cancellationToken).ConfigureAwait(false));
        }

        return new TenseHeadOutcome(blob, reread.Tense!, report.ToString());
    }

    /// <summary>
    ///     Reports the dev accuracy and log loss of the tense head combined with the Basic model at each weight.
    /// </summary>
    /// <param name="head">The head, as the library reads it.</param>
    /// <param name="table">The tokens of the development split.</param>
    /// <param name="rows">The development rows.</param>
    /// <param name="dev">The sentences of the development split, in the order of the table.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>The Markdown table of the report.</returns>
    private static async Task<string> FusionReportAsync(TenseHead head, PosTokenTable table, TenseRows rows, List<PosSentence> dev, CancellationToken cancellationToken)
    {
        // The share of the past analyses among the verb analyses of the Basic result, for every row (null: Basic has none)
        var basic = new double?[rows.Count];
        var rowAt = new Dictionary<int, int>();
        for (var r = 0; r < rows.Count; r++)
        {
            rowAt[rows.Tokens[r]] = r;
        }

        using var model = new BasicWordSenseModel();
        var at = 0;
        foreach (var sentence in dev)
        {
            var results = await model.ClassifyAsync(sentence.Words, cancellationToken).ConfigureAwait(false);
            for (var j = 0; j < results.Length; j++, at++)
            {
                if (rowAt.TryGetValue(at, out var r))
                {
                    basic[r] = TenseFusion.PastShare(results[j].Analyses);
                }
            }
        }

        var headScore = Score(head, table, rows);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Combination with the Basic model ({basic.Count(b => b is not null)} of {rows.Count} development rows have a Basic share; the odds of the head to the power w times the odds of the Basic share to the power 1 - w):\n\n");
        text.Append("| w | Dev accuracy | Dev log loss |\n| ---: | ---: | ---: |\n");
        foreach (var w in FusionGrid)
        {
            var correct = 0;
            var loss = 0.0;
            for (var r = 0; r < rows.Count; r++)
            {
                var q = TenseFusion.Fuse(headScore.Probability[r], basic[r], w);
                correct += (q > 0.5) == rows.Past[r] ? 1 : 0;
                loss -= Math.Log(Math.Max(rows.Past[r] ? q : 1.0 - q, 1e-12));
            }

            text.Append(CultureInfo.InvariantCulture, $"| {w:0.0} | {100.0 * correct / rows.Count:0.00}% ({correct}/{rows.Count}) | {loss / rows.Count:0.0000} |\n");
        }

        return text.ToString();
    }

    /// <summary>
    ///     Makes the head as the library reads it, for scoring the development rows with the arithmetic of inference.
    /// </summary>
    /// <param name="layout">The feature layout.</param>
    /// <param name="c">The regularization strength that the head was fitted with.</param>
    /// <param name="weights">The weights, in the order of the layout.</param>
    /// <param name="bias">The bias.</param>
    /// <param name="hiddenSize">The hidden size of the encoder.</param>
    /// <returns>The head.</returns>
    private static TenseHead Make(int layout, double c, float[] weights, float bias, int hiddenSize) =>
        new(new TenseHeadData(layout, (float)c, weights, bias), hiddenSize);

    /// <summary>
    ///     Gets the name of a layout for the report and the progress lines.
    /// </summary>
    /// <param name="layout">The feature layout.</param>
    /// <returns><c>word</c> for the vector of the word, <c>word+-1</c> for the word and its neighbors.</returns>
    private static string LayoutName(int layout) => layout == PosHeadBlob.TenseWord ? "word" : "word+-1";

    /// <summary>
    ///     Gets the number of features of a layout, which is the length of the weights of a head of that layout.
    /// </summary>
    /// <param name="layout">The feature layout.</param>
    /// <param name="dimension">The hidden size of the encoder.</param>
    /// <returns>One vector for the word layout, three for the layout with neighbors.</returns>
    /// <exception cref="ArgumentException">Thrown when the layout is unknown.</exception>
    private static int FeatureCount(int layout, int dimension) => layout switch
    {
        PosHeadBlob.TenseWord => dimension,
        PosHeadBlob.TenseWordAndNeighbors => 3 * dimension,
        _ => throw new ArgumentException($"The tense layout {layout} is unknown.", nameof(layout)),
    };

    /// <summary>
    ///     Builds the features of a token: its vector, or the vectors of the previous word, the word and the next word with
    ///     zeros beyond the sentence edge and for a word without word pieces.
    /// </summary>
    private static double[] Features(PosTokenTable table, int token, int layout)
    {
        var d = table.Dimension;
        var features = new double[FeatureCount(layout, d)];
        if (layout == PosHeadBlob.TenseWord)
        {
            Copy(table, token, features, 0);
            return features;
        }

        if (table.HasPrevious(token) && table.HasVector[token - 1])
        {
            Copy(table, token - 1, features, 0);
        }

        Copy(table, token, features, d);
        if (table.HasNext(token) && table.HasVector[token + 1])
        {
            Copy(table, token + 1, features, 2 * d);
        }

        return features;
    }

    /// <summary>
    ///     Copies the vector of a token into a feature array, where the features of one layout are placed one vector
    ///     after the other.
    /// </summary>
    /// <param name="table">The tokens.</param>
    /// <param name="token">The index of the token whose vector is copied.</param>
    /// <param name="target">The feature array.</param>
    /// <param name="offset">The index in <paramref name="target"/> of the first value of the vector.</param>
    private static void Copy(PosTokenTable table, int token, double[] target, int offset)
    {
        var d = table.Dimension;
        for (var j = 0; j < d; j++)
        {
            target[offset + j] = table.Vectors[(token * d) + j];
        }
    }

    /// <summary>
    ///     Gets the vector of a token as doubles.
    /// </summary>
    /// <param name="table">The tokens.</param>
    /// <param name="token">The index of the token.</param>
    /// <returns>The vector, or null when the token has no word pieces and so no vector.</returns>
    private static double[]? Vector(PosTokenTable table, int token)
    {
        if (!table.HasVector[token])
        {
            return null;
        }

        var d = table.Dimension;
        var v = new double[d];
        for (var j = 0; j < d; j++)
        {
            v[j] = table.Vectors[(token * d) + j];
        }

        return v;
    }
}
