using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     One evaluation sentence with its centered, normalized feature.
/// </summary>
/// <param name="Set">The set the row was read for: <c>holdout</c> or <c>real-eval</c>.</param>
/// <param name="Origin">The provenance origin of the row (<c>real</c>, <c>authored</c> or <c>llm-corpus</c>).</param>
/// <param name="Row">The corpus row.</param>
/// <param name="Feature">The centered and normalized feature of the target word.</param>
/// <param name="SenseIndex">The index of the gold sense in the lemma's sense list.</param>
internal sealed record EvalRow(string Set, string Origin, CorpusRow Row, float[] Feature, int SenseIndex)
{
    /// <summary>
    ///     Gets the reporting sets the row belongs to: a holdout row is in <c>holdout</c> and, when its origin is
    ///     <c>real</c>, also in <c>real</c>; a real-evaluation row is only in <c>real-eval</c>.
    /// </summary>
    public IEnumerable<string> Memberships
    {
        get
        {
            if (Set == AnalysisFiles.SetRealEval)
            {
                yield return AnalysisFiles.SetRealEval;
                yield break;
            }

            yield return AnalysisFiles.SetHoldout;
            if (Origin == "real")
            {
                yield return AnalysisFiles.SetReal;
            }
        }
    }
}

/// <summary>
///     The anchors, evaluation rows and abstain margin of one lemma, as the analysis commands need them.
/// </summary>
/// <param name="Lemma">The lemma.</param>
/// <param name="Senses">The senses of the lemma in ordinal order of label.</param>
/// <param name="Names">The sense labels, parallel to the senses.</param>
/// <param name="Kind">Binary or one-versus-rest.</param>
/// <param name="Margin">The abstain margin chosen for the full anchor set, as the table build chooses it.</param>
/// <param name="AnchorFeatures">The centered and normalized anchor features.</param>
/// <param name="AnchorSense">The sense index of each anchor.</param>
/// <param name="Eval">The evaluation rows of the lemma.</param>
internal sealed record LemmaAnalysis(
    string Lemma,
    SenseDefinition[] Senses,
    string[] Names,
    LemmaModelKind Kind,
    float Margin,
    float[][] AnchorFeatures,
    int[] AnchorSense,
    IReadOnlyList<EvalRow> Eval)
{
    /// <summary>
    ///     Trains the lemma's classifier on a subset of its anchors with the settings of the table build.
    /// </summary>
    /// <param name="anchorIndexes">The indexes into <see cref="AnchorFeatures"/>.</param>
    /// <param name="c">The inverse regularization strength.</param>
    /// <returns>The model, using the lemma's abstain margin.</returns>
    public LemmaModel Train(IReadOnlyList<int> anchorIndexes, double c)
    {
        var x = anchorIndexes.Select(i => AnchorFeatures[i]).ToArray();
        var labels = anchorIndexes.Select(i => AnchorSense[i]).ToArray();
        var (weights, bias) = TableBuilder.Fit(x, labels, Senses.Length, Kind, c);
        return new LemmaModel(Lemma, Senses, Kind, weights, bias, Margin, x[0].Length);
    }
}

/// <summary>
///     The featurized corpus shared by the analysis commands: embeddings are computed once per sentence.
/// </summary>
internal sealed class AnalysisData
{
    private AnalysisData(IReadOnlyList<LemmaAnalysis> lemmas, double c)
    {
        Lemmas = lemmas;
        C = c;
    }

    /// <summary>
    ///     Gets the lemmas in ordinal order.
    /// </summary>
    public IReadOnlyList<LemmaAnalysis> Lemmas { get; }

    /// <summary>
    ///     Gets the inverse regularization strength used to train every classifier.
    /// </summary>
    public double C { get; }

    /// <summary>
    ///     Reads the corpus, encodes every sentence once and prepares the per-lemma data.
    /// </summary>
    /// <param name="encoder">The encoder.</param>
    /// <param name="tokenizer">The tokenizer that matches the encoder.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="lemmaFilter">The lemmas to analyze, or <see langword="null"/> for all.</param>
    /// <param name="options">The builder settings.</param>
    /// <returns>The prepared data.</returns>
    public static AnalysisData Load(
        IWordSenseEncoder encoder,
        WordPieceTokenizer tokenizer,
        string corpus,
        IReadOnlySet<string>? lemmaFilter,
        TableBuilderOptions options)
    {
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(tokenizer);
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var evalPath = Path.Combine(corpus, RealEval.EvalFile);
        var realEval = File.Exists(evalPath) ? Corpus.ReadSentences(evalPath) : [];
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var origins = new Dictionary<string, string>(StringComparer.Ordinal);
        var provenance = Path.Combine(corpus, "provenance");
        if (Directory.Exists(provenance))
        {
            foreach (var file in Directory.GetFiles(provenance, "*.tsv").OrderBy(f => f, StringComparer.Ordinal))
            {
                foreach (var row in RealImport.ReadProvenance(file))
                {
                    origins[row.Id] = row.Origin;
                }
            }
        }

        return Create(rows => TableBuilder.Pool(encoder, tokenizer, rows, options), anchors, holdout, realEval, origins, inventory, options, lemmaFilter);
    }

    /// <summary>
    ///     Builds the per-lemma data from corpus rows and a function that produces pooled target vectors.
    /// </summary>
    /// <param name="pool">Returns one pooled vector per row, in row order.</param>
    /// <param name="anchors">The training rows.</param>
    /// <param name="holdout">The holdout rows.</param>
    /// <param name="realEval">The real-text evaluation rows.</param>
    /// <param name="origins">The provenance origin by row id; a row without an entry has origin <c>llm-corpus</c>.</param>
    /// <param name="inventory">The sense inventory.</param>
    /// <param name="options">The builder settings.</param>
    /// <param name="lemmaFilter">The lemmas to analyze, or <see langword="null"/> for all.</param>
    /// <returns>The prepared data.</returns>
    /// <exception cref="InvalidDataException">Thrown when there are no anchors or a lemma has fewer than two senses.</exception>
    public static AnalysisData Create(
        Func<IReadOnlyList<CorpusRow>, double[][]> pool,
        IReadOnlyList<CorpusRow> anchors,
        IReadOnlyList<CorpusRow> holdout,
        IReadOnlyList<CorpusRow> realEval,
        IReadOnlyDictionary<string, string> origins,
        SenseInventory inventory,
        TableBuilderOptions options,
        IReadOnlySet<string>? lemmaFilter)
    {
        ArgumentNullException.ThrowIfNull(pool);
        ArgumentNullException.ThrowIfNull(inventory);
        if (anchors.Count == 0)
        {
            throw new InvalidDataException("The corpus has no anchor sentences.");
        }

        // The anchor mean is taken over all anchors, as in the table build, so the features equal the table's
        var pooled = pool(anchors);
        var dim = pooled[0].Length;
        var mean = new float[dim];
        foreach (var vector in pooled)
        {
            for (var d = 0; d < dim; d++)
            {
                mean[d] += (float)(vector[d] / pooled.Length);
            }
        }

        var anchorFeatures = TableBuilder.Center(pooled, mean, anchors);
        var anchorLemmas = anchors.Select(r => r.Lemma).ToHashSet(StringComparer.Ordinal);
        bool Selected(string lemma) => anchorLemmas.Contains(lemma) && (lemmaFilter is null || lemmaFilter.Contains(lemma));
        var evalSource = holdout.Select(r => (Set: AnalysisFiles.SetHoldout, Row: r))
            .Concat(realEval.Select(r => (Set: AnalysisFiles.SetRealEval, Row: r)))
            .Where(t => Selected(t.Row.Lemma) && inventory.Contains(t.Row.Lemma, t.Row.Sense))
            .ToList();
        var evalRows = evalSource.Select(t => t.Row).ToList();
        var evalFeatures = evalRows.Count == 0 ? [] : TableBuilder.Center(pool(evalRows), mean, evalRows);

        var lemmas = anchors.Select((row, i) => (row, i)).Where(t => Selected(t.row.Lemma))
            .GroupBy(t => t.row.Lemma, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        var evalByLemma = evalSource.Select((t, i) => (t, i)).ToLookup(x => x.t.Row.Lemma, StringComparer.Ordinal);
        var trained = new TrainedLemma[lemmas.Count];
        Parallel.For(0, lemmas.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, options.Parallelism) }, k =>
        {
            trained[k] = TableBuilder.TrainLemma(lemmas[k].Key, lemmas[k].Select(t => t.i).ToArray(), anchors, anchorFeatures, inventory, options);
        });

        // A lemma too small for cross-validation takes the median margin, as in the table build
        var margins = trained.Where(t => t.Cv).Select(t => t.Margin).OrderBy(m => m).ToList();
        var fallback = margins.Count == 0 ? 0f : margins[margins.Count / 2];
        var result = new List<LemmaAnalysis>();
        for (var k = 0; k < lemmas.Count; k++)
        {
            var t = trained[k];
            var lemma = lemmas[k].Key;
            var index = lemmas[k].Select(x => x.i).ToArray();
            var eval = evalByLemma[lemma].Select(x => new EvalRow(
                x.t.Set,
                origins.GetValueOrDefault(PosAudit.RowId(x.t.Row.Lemma, x.t.Row.Sentence), x.t.Set == AnalysisFiles.SetRealEval ? "real" : "llm-corpus"),
                x.t.Row,
                evalFeatures[x.i],
                Array.BinarySearch(t.Names, x.t.Row.Sense, StringComparer.Ordinal))).ToList();
            result.Add(new LemmaAnalysis(lemma, t.Senses, t.Names, t.Kind, t.Cv ? t.Margin : fallback, index.Select(i => anchorFeatures[i]).ToArray(), t.Labels, eval));
        }

        return new AnalysisData(result, options.C);
    }
}
