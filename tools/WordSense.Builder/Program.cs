using System.Globalization;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The command-line entry point of the builder, the build-time tool that creates the word table and the model
///     weights shipped in the WordSense packages.
/// </summary>
/// <remarks>
///     <para>
///     Commands (options are <c>--name value</c> pairs; every path defaults to the repository layout below the current
///     directory):
///     </para>
///     <list type="bullet">
///     <item><description><c>compile-image [--data DIR] [--corpus DIR] [--output FILE]</c> compiles the data tables into the word image of the core package (<c>Resources/wordsense.img</c>).</description></item>
///     <item><description><c>audit-forms [--data DIR] [--sample 300] [--seed N] [--oracle FILE] [--adjudications FILE] [--output FILE] [--max-error 0.01]</c> checks a seeded stratified sample of the inflected form rows per provenance against the LemmInflect oracle (a pip-installed checker, not shipped), reports the error rate with a 95% Wilson interval per stratum and fails when a generated stratum reaches the limit.</description></item>
///     <item><description><c>rebuild-forms [--data DIR] [--corpus DIR] [--oewn FILE] [--agid FILE] [--output DIR]</c> regenerates the entry and form tables from the entries, the committed legacy analyses (<c>corpus/lexicon/form-frozen.tsv</c>), the irregular lists, the written forms of the Open English WordNet file and the dictionary; a generated inflection ships only when a second source corroborates it (the AGID <c>infl.txt</c> file attests forms). Both inputs must match their pinned SHA-256; the default places are <c>artifacts/inputs/english-wordnet-2025.xml.gz</c> and <c>artifacts/inputs/agid/agid-2016.01.19/infl.txt</c>. The result depends on nothing else, so it is reproducible.</description></item>
///     <item><description><c>import-agid --input FILE [--corpus DIR] [--output FILE]</c> derives the irregular forms of the dictionary words from the inflection database file <c>infl.txt</c>.</description></item>
///     <item><description><c>import-moby --input FILE [--corpus DIR] [--output FILE]</c> imports the pinned Moby Part-of-Speech II list into the dictionary word list of the corpus.</description></item>
///     <item><description><c>weights &lt;modelId&gt; [--corpus DIR] [--repository DIR] [--cache DIR] [--no-cache] [--recompute-mean] [--solver kernel|primal] [--calibration-target P]</c> trains the fine-sense weights of an ONNX model (minilm, bert-base-cased) from <c>corpus/anchors</c> and writes them to its package; the thresholds of each lemma are first chosen by cross-validation on its anchors and then moved to the precision target measured on the real-text calibration sentences of <c>corpus/holdout</c> (the real held-out sentences of the calibration documents, see the real-text split; <c>--calibration-target P</c> sets the lemma-level target, 0.97 by default), and the half-precision storage is checked against <c>corpus/holdout</c>. Pooled vectors are cached in <c>DIR/&lt;model id&gt;</c> (default <c>.cache/embeddings</c> of the repository) so only new sentences are encoded; <c>--no-cache</c> bypasses the cache; the global mean of the existing weights is reused unless <c>--recompute-mean</c> is given. The classifiers are fitted by Newton's method in the span of the anchors of the lemma (<c>--solver kernel</c>, the default, which is fast when a lemma has fewer anchors than the encoder has dimensions); <c>--solver primal</c> is the slow reference in the feature space that finds the same optimum.</description></item>
///     <item><description><c>evaluate &lt;modelId&gt; [--corpus DIR] [--repository DIR] [--output FILE] [--table FILE] [--outcomes FILE]</c> measures a shipped model (minilm, bert-base-cased, basic) on <c>corpus/holdout</c>: the share of the sentences answered at each level and the precision at each level; <c>--table</c> updates the row of the model in the model comparison table file; <c>--outcomes</c> writes the answer for every sentence.</description></item>
///     <item><description><c>ud-counts [--ud DIR] [--corpus DIR] [--output DIR]</c> downloads and verifies the pinned treebank files and writes the count files of <c>corpus/ud</c>.</description></item>
///     <item><description><c>train-basic [--corpus DIR] [--repository DIR] [--ud DIR] [--refresh-ud true] [--output FILE]</c> retrains the basic model completely and writes its table; it reads the legacy class-labeled data of <c>corpus/legacy</c> (anchors, holdout, senses, forms) because the shipped table was trained from it, and regenerates the treebank counts first when they are missing or <c>--refresh-ud true</c> is given.</description></item>
///     <item><description><c>tune-basic [--corpus DIR] [--repository DIR] [--ud DIR]</c> scores the hyper-parameter grid on the treebank development file and the legacy anchor dev sentences; it writes nothing.</description></item>
///     <item><description><c>ud-irregular [--ud DIR] [--corpus DIR] [--output FILE]</c> derives the irregular inflected forms of the dictionary words from the pinned treebank training and development files.</description></item>
///     <item><description><c>coverage --text DIR [--corpus DIR] [--top N] [--output FILE]</c> reports how many of the running words of the text files of a directory the word table knows, the share of the known words whose lemma is trained (a lemma that has sentences in <c>corpus/anchors</c>), and lists the most frequent unknown words, split into name-like and other words.</description></item>
///     <item><description><c>appendix [--corpus DIR] [--data DIR] [--output FILE]</c> writes the generated senses appendix of the user guide: the senses of the trained lemmas of <c>corpus/anchors</c> with id, group, sense key, gloss and respelling, from the data folder (default <c>data</c>).</description></item>
///     <item><description><c>import-oewn --input FILE [--output DIR] [--sha256 HEX|none] [--threshold N] [--overwrite true]</c> imports the pinned Open English WordNet release into the table files of the data folder (default <c>data</c>) and refuses to replace existing tables unless <c>--overwrite true</c> is given; <c>import-oewn --previous FILE [--previous-release ID] [--previous-sha256 HEX|none] [--output DIR]</c> instead writes the identifier map from a previous release.</description></item>
///     <item><description><c>import-wn30-counts --input FILE [--data DIR] [--sha256 HEX|none]</c> imports the pinned WordNet 3.0 sense index (<c>index.sense</c>) into the <c>sense_frequency</c> table, joined on the sense key, with the count 0 for a sense that has no line.</description></item>
///     <item><description><c>label-senses [--data DIR]</c> gives every sense of the inventory that has no label a readable label; labels that exist are never changed and no group is created.</description></item>
///     <item><description><c>seed-inventory [--data DIR] [--curation DIR]</c> adds a project sense to every closed-class entry that has none and imports the curated labels and groups of the curation folder (default <c>corpus/curation</c>), leaving out a group with one sense and a group with every sense of its lemma and part of speech; it is the bootstrap of the inventory and is idempotent.</description></item>
///     <item><description><c>validate-data [--data DIR] [--baseline DIR]</c> validates the table files of the data folder against its <c>schema.yaml</c> and, with a baseline (the data folder of the last release), checks that published sense labels are neither changed nor reused; it exits with 1 and lists the violations when the data is not valid.</description></item>
///     </list>
/// </remarks>
internal static class Program
{
    private const string Usage = "Usage: WordSense.Builder <command> [--name value ...]. Commands: compile-image, rebuild-forms, audit-forms, generate-facts, import-agid, import-moby, weights <modelId>, evaluate <modelId>, ud-counts, ud-irregular, coverage, train-basic, tune-basic, appendix, import-oewn, import-wn30-counts, label-senses, seed-inventory, validate-data.";

    /// <summary>The name of the option that gives the input file of an import command.</summary>
    private const string InputOption = "input";

    /// <summary>
    ///     Runs the builder.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> Main(string[] args)
    {
        return await RunAsync(args, Console.Out, Console.Error, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    ///     Runs the builder with explicit output writers.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">The standard output.</param>
    /// <param name="error">The error output.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>0 on success, 1 on a usage or data error.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter output, TextWriter error, CancellationToken cancellationToken)
    {
        try
        {
            if (args.Length == 0)
            {
                error.WriteLine(Usage);
                return 1;
            }

            // The model id of weights and evaluate may be given as the second argument instead of --model
            var rest = args.Skip(1).ToList();
            if (args[0] is "weights" or "evaluate" && rest.Count > 0 && !rest[0].StartsWith("--", StringComparison.Ordinal))
            {
                rest.InsertRange(0, ["--model"]);
            }

            var options = ParseOptions(rest.ToArray());
            var repository = Path.GetFullPath(options.GetValueOrDefault("repository", "."));
            var corpus = options.TryGetValue("corpus", out var c) ? Path.GetFullPath(c) : Path.Join(repository, "corpus");
            switch (args[0])
            {
                case "compile-image":
                    return RunCompileImage(options, repository, output);
                case "import-moby":
                    return RunImportMoby(options, corpus, output);
                case "weights":
                    await ModelTrainer.TrainAsync(repository, corpus, ModelCatalog.Find(Require(options, "model")), TrainSettingsOf(options, repository), output, cancellationToken).ConfigureAwait(false);
                    return 0;
                case "ud-counts":
                    return await RunUdCountsAsync(options, repository, corpus, output, cancellationToken).ConfigureAwait(false);
                case "train-basic":
                    return await RunTrainBasicAsync(options, repository, corpus, output, cancellationToken).ConfigureAwait(false);
                case "tune-basic":
                    var tuneCache = options.TryGetValue("ud", out var ud3) ? Path.GetFullPath(ud3) : BasicModelBuilder.DefaultUdCache(repository);
                    output.Write(await BasicTuner.RunAsync(corpus, tuneCache, output, cancellationToken).ConfigureAwait(false));
                    return 0;
                case "evaluate":
                    return await RunEvaluateAsync(options, repository, corpus, output, cancellationToken).ConfigureAwait(false);
                case "ud-irregular":
                    return RunUdIrregular(options, repository, corpus, output);
                case "coverage":
                    return RunCoverage(options, corpus, output);
                case "import-oewn":
                    return RunImportOewn(options, repository, output);
                case "validate-data":
                    return RunValidateData(options, repository, output, error);
                case "import-wn30-counts":
                    return RunImportWn30Counts(options, repository, output);
                case "label-senses":
                    return RunLabelSenses(options, repository, output);
                case "seed-inventory":
                    return RunSeedInventory(options, repository, corpus, output);
                case "rebuild-forms":
                    return RunRebuildForms(options, repository, corpus, output);
                case "generate-facts":
                    return RunGenerateFacts(options, repository, corpus, output, error);
                case "audit-forms":
                    return RunAuditForms(options, repository, output, error);
                case "import-agid":
                    return RunImportAgid(options, corpus, output);
                case "appendix":
                    return RunAppendix(options, repository, corpus, output);
                default:
                    error.WriteLine($"Unknown command '{args[0]}'. {Usage}");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or HttpRequestException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static readonly HashSet<string> KnownOptions = new(["repository", "corpus", "output", "model", "ud", "refresh-ud", "table", InputOption, "text", "top", "data", "sha256", "threshold", "overwrite", "outcomes", "oewn", "agid", "sample", "seed", "oracle", "adjudications", "max-error", "previous", "previous-release", "previous-sha256", "cache", "no-cache", "recompute-mean", "baseline", "curation", "solver", "calibration-target"], StringComparer.Ordinal);

    /// <summary>
    ///     Options that are switches: they take no value, but accept a following <c>true</c> or <c>false</c>.
    /// </summary>
    private static readonly HashSet<string> FlagOptions = new(["no-cache", "recompute-mean"], StringComparer.Ordinal);

    /// <summary>
    ///     Builds the settings of a weights run from the options.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="repository">The repository root.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="ArgumentException">Thrown when <c>--cache</c> and <c>--no-cache</c> are both given.</exception>
    internal static TrainSettings TrainSettingsOf(Dictionary<string, string> options, string repository)
    {
        var noCache = IsOn(options, "no-cache");
        if (noCache && options.ContainsKey("cache"))
        {
            throw new ArgumentException($"The options --cache and --no-cache cannot be combined. {Usage}");
        }

        string? root = null;
        if (!noCache)
        {
            root = options.TryGetValue("cache", out var cache) ? Path.GetFullPath(cache) : TrainSettings.DefaultCacheRoot(repository);
        }

        var solver = FitSolver.Kernel;
        if (options.TryGetValue("solver", out var solverName) && !Enum.TryParse(solverName, true, out solver))
        {
            throw new ArgumentException($"The option --solver must be 'kernel' or 'primal', not '{solverName}'. {Usage}");
        }

        var calibrationTarget = RealCalibrator.DefaultLemmaTarget;
        if (options.TryGetValue("calibration-target", out var targetText)
            && (!double.TryParse(targetText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out calibrationTarget) || calibrationTarget is <= 0.5 or >= 1))
        {
            throw new ArgumentException($"The option --calibration-target must be a precision between 0.5 and 1, not '{targetText}'. {Usage}");
        }

        return new TrainSettings(root, IsOn(options, "recompute-mean"), solver, calibrationTarget);
    }

    private static bool IsOn(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) && !string.Equals(value, "false", StringComparison.Ordinal);

    private static int RunCompileImage(Dictionary<string, string> options, string repository, TextWriter output)
    {
        var dataFolder = options.TryGetValue("data", out var cd) ? Path.GetFullPath(cd) : Path.Join(repository, "data");
        var imageOutput = options.TryGetValue("output", out var imo) ? imo : Path.Join(repository, Data.ImageCompiler.DefaultOutput);
        var image = Data.ImageCompiler.Compile(dataFolder, imageOutput);
        output.WriteLine($"Wrote the word image to {imageOutput}: {image.Spellings.ToString(CultureInfo.InvariantCulture)} spellings, {image.Analyses.ToString(CultureInfo.InvariantCulture)} analyses, {image.Entries.ToString(CultureInfo.InvariantCulture)} entries, {image.Senses.ToString(CultureInfo.InvariantCulture)} senses, {image.Groups.ToString(CultureInfo.InvariantCulture)} groups, {image.Bytes.ToString(CultureInfo.InvariantCulture)} bytes.");
        return 0;
    }

    private static int RunImportMoby(Dictionary<string, string> options, string corpus, TextWriter output)
    {
        var mobyInput = Require(options, InputOption);
        var lexiconOutput = options.TryGetValue("output", out var lo) ? lo : Path.Join(corpus, "lexicon", LexiconSource.LexiconFile);
        var imported = LexiconImporter.Import(mobyInput, lexiconOutput);
        output.WriteLine(LexiconImporter.Render(imported));
        return 0;
    }

    private static async Task<int> RunUdCountsAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var udDirectory = options.TryGetValue("ud", out var ud) ? Path.GetFullPath(ud) : BasicModelBuilder.DefaultUdCache(repository);
        var countsOutput = options.TryGetValue("output", out var co) ? Path.GetFullPath(co) : Path.Join(corpus, "ud");
        output.WriteLine(await BasicModelBuilder.WriteUdCountsAsync(udDirectory, countsOutput, cancellationToken).ConfigureAwait(false));
        return 0;
    }

    private static async Task<int> RunTrainBasicAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var cache = options.TryGetValue("ud", out var ud) ? Path.GetFullPath(ud) : BasicModelBuilder.DefaultUdCache(repository);
        var refresh = options.TryGetValue("refresh-ud", out var r) && string.Equals(r, "true", StringComparison.OrdinalIgnoreCase);
        var tablePath = options.TryGetValue("output", out var to) ? to : BasicModelBuilder.DefaultOutput;
        await BasicModelBuilder.TrainAsync(repository, corpus, cache, refresh, tablePath, output, cancellationToken).ConfigureAwait(false);
        return 0;
    }

    private static async Task<int> RunEvaluateAsync(Dictionary<string, string> options, string repository, string corpus, TextWriter output, CancellationToken cancellationToken)
    {
        var result = await Evaluator.RunAsync(repository, corpus, ModelCatalog.Find(Require(options, "model")), output, cancellationToken, options.GetValueOrDefault("outcomes")).ConfigureAwait(false);
        var text = result.Render();
        output.Write(text);
        if (options.TryGetValue("output", out var report))
        {
            File.WriteAllText(report, text, new System.Text.UTF8Encoding(false));
        }

        if (options.TryGetValue("table", out var tableFile))
        {
            result.UpdateTableFile(tableFile);
        }

        return 0;
    }

    private static int RunUdIrregular(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var irregularCache = options.TryGetValue("ud", out var ud) ? Path.GetFullPath(ud) : BasicModelBuilder.DefaultUdCache(repository);
        var irregularOutput = options.TryGetValue("output", out var io) ? io : Path.Join(corpus, "lexicon", IrregularForms.DerivedFile);
        output.WriteLine(IrregularForms.Derive(irregularCache, corpus, irregularOutput));
        return 0;
    }

    private static int RunCoverage(Dictionary<string, string> options, string corpus, TextWriter output)
    {
        var textDirectory = Path.GetFullPath(Require(options, "text"));
        var topCount = options.TryGetValue("top", out var tp) ? int.Parse(tp, CultureInfo.InvariantCulture) : 100;
        var anchorsDirectory = Path.Join(corpus, "anchors");
        if (!Directory.Exists(anchorsDirectory))
        {
            throw new InvalidDataException($"The directory '{anchorsDirectory}' was not found.");
        }

        var trainedLemmas = Corpus.ReadLemmas(anchorsDirectory);
        var table = DemaConsulting.WordSense.WordLexicon.Current;
        var coverage = CoverageReport.Read(
            textDirectory,
            w => table.TryLookup(w, out _),
            w => table.TryLookup(w, out var analyses) && analyses.Any(a => trainedLemmas.Contains(a.Lemma))).Render(topCount);
        output.Write(coverage);
        if (options.TryGetValue("output", out var coverageFile))
        {
            File.WriteAllText(coverageFile, coverage, new System.Text.UTF8Encoding(false));
        }

        return 0;
    }

    private static int RunRebuildForms(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var formsData = options.TryGetValue("data", out var bd) ? Path.GetFullPath(bd) : Path.Join(repository, "data");
        var oewnFile = FormInput(options, "oewn", Path.Join(repository, DefaultOewnInput), "Open English WordNet");
        var agidFile = FormInput(options, "agid", Path.Join(repository, DefaultAgidInput), "inflection database");
        var generated = Data.FormGenerator.Run(formsData, corpus, oewnFile, agidFile, options.TryGetValue("output", out var fo) ? Path.GetFullPath(fo) : null);
        output.Write(generated.Render());
        return 0;
    }

    private static int RunImportAgid(Dictionary<string, string> options, string corpus, TextWriter output)
    {
        var agidOutput = options.TryGetValue("output", out var ao) ? ao : Path.Join(corpus, "lexicon", Data.IrregularSources.AgidFile);
        output.WriteLine(Data.AgidImporter.Import(Require(options, InputOption), corpus, agidOutput));
        return 0;
    }

    private static int RunAppendix(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var appendix = options.TryGetValue("output", out var a) ? a : Path.Join(repository, "docs", "user_guide", "appendix-senses.md");
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var lemmas = AppendixGenerator.Write(corpus, data, appendix);
        output.WriteLine($"Wrote the appendix of {lemmas.ToString(CultureInfo.InvariantCulture)} lemmas to {appendix}.");
        return 0;
    }

    private const string DefaultOewnInput = "artifacts/inputs/english-wordnet-2025.xml.gz";

    private const string DefaultAgidInput = "artifacts/inputs/agid/agid-2016.01.19/infl.txt";

    // The pinned input of the form tables: the option, else the default place; a missing file is reported with where to get it
    private static string FormInput(Dictionary<string, string> options, string name, string fallback, string title)
    {
        var path = Path.GetFullPath(options.TryGetValue(name, out var given) ? given : fallback);
        return File.Exists(path)
            ? path
            : throw new InvalidDataException($"The pinned {title} file '{path}' does not exist; download it (see data/PROVENANCE.md) and give its place with --{name}.");
    }

    private static int RunImportOewn(Dictionary<string, string> options, string repository, TextWriter output)
    {
        var data = options.TryGetValue("output", out var o) ? Path.GetFullPath(o) : Path.Join(repository, "data");
        if (options.TryGetValue("previous", out var previous))
        {
            var previousSha = options.TryGetValue("previous-sha256", out var ps) ? ps : Data.OewnImporter.PreviousPinnedSha256;
            var map = Data.OewnImporter.ImportPrevious(previous, data, options.GetValueOrDefault("previous-release", Data.OewnImporter.PreviousRelease), string.Equals(previousSha, "none", StringComparison.OrdinalIgnoreCase) ? null : previousSha);
            output.WriteLine(Data.OewnImporter.Describe(map));
            return 0;
        }

        var sha = options.TryGetValue("sha256", out var s) ? s : Data.OewnImporter.PinnedSha256;
        var threshold = options.TryGetValue("threshold", out var t) ? int.Parse(t, CultureInfo.InvariantCulture) : Data.OewnImporter.DefaultSplitThreshold;
        var overwrite = options.TryGetValue("overwrite", out var w) && string.Equals(w, "true", StringComparison.OrdinalIgnoreCase);
        var result = Data.OewnImporter.Import(Require(options, InputOption), data, Path.Join(data, "schema.yaml"), string.Equals(sha, "none", StringComparison.OrdinalIgnoreCase) ? null : sha, threshold, overwrite);
        output.Write(result.Render());
        return 0;
    }

    private static int RunGenerateFacts(Dictionary<string, string> options, string repository, string corpus, TextWriter output, TextWriter error)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var sample = options.TryGetValue("sample", out var sm) ? int.Parse(sm, CultureInfo.InvariantCulture) : Data.FactGenerator.DefaultSample;
        var seed = options.TryGetValue("seed", out var sd) ? int.Parse(sd, CultureInfo.InvariantCulture) : Data.FactGenerator.DefaultSeed;
        var generated = Data.FactGenerator.Generate(data, corpus, seed, sample);
        var failures = Data.FactGenerator.Check(generated.Facts);
        var (checkedCount, withGerund, missing) = Data.FactGenerator.CheckIngEntries(data);
        output.WriteLine($"Seed {seed.ToString(CultureInfo.InvariantCulture)}, sample {sample.ToString(CultureInfo.InvariantCulture)}: {generated.Facts.Count.ToString(CultureInfo.InvariantCulture)} facts ({string.Join(", ", generated.Summary.Select(p => $"{p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}"))}).");
        output.WriteLine($"{failures.Count.ToString(CultureInfo.InvariantCulture)} fact(s) do not hold in the shipped word table.");
        output.WriteLine($"Withheld (not asserted): {generated.Withheld.Count.ToString(CultureInfo.InvariantCulture)} ({string.Join(", ", generated.Withheld.GroupBy(w => w.Category + " " + w.Reason).Select(g => $"{g.Key} {g.Count().ToString(CultureInfo.InvariantCulture)}"))}).");
        output.WriteLine($"-ing noun and adjective entries: {checkedCount.ToString(CultureInfo.InvariantCulture)} checked, {withGerund.ToString(CultureInfo.InvariantCulture)} also have the verb gerund analysis, {missing.Count.ToString(CultureInfo.InvariantCulture)} lack the entry analysis.");
        foreach (var line in failures.Concat(missing.Select(m => "ing-missing\t" + m)))
        {
            error.WriteLine(line);
        }

        if (options.TryGetValue("output", out var file))
        {
            File.WriteAllText(file, Data.FactGenerator.Render(generated.Facts), new System.Text.UTF8Encoding(false));
        }

        return failures.Count == 0 && missing.Count == 0 ? 0 : 1;
    }

    private static int RunAuditForms(Dictionary<string, string> options, string repository, TextWriter output, TextWriter error)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var sample = options.TryGetValue("sample", out var sm) ? int.Parse(sm, CultureInfo.InvariantCulture) : 300;
        var seed = options.TryGetValue("seed", out var sd) ? int.Parse(sd, CultureInfo.InvariantCulture) : 20240601;
        var maxError = options.TryGetValue("max-error", out var me) ? double.Parse(me, CultureInfo.InvariantCulture) : 0.01;
        if (sample <= 0)
        {
            throw new ArgumentException("The --sample value must be a positive number of rows per stratum.");
        }

        if (!(maxError > 0 && maxError <= 1))
        {
            throw new ArgumentException("The --max-error value must be greater than 0 and at most 1.");
        }

        var supplied = options.TryGetValue("oracle", out var oracle) ? Data.FormAuditor.ReadVerdicts(oracle) : null;
        var (rows, population) = Data.FormAuditor.Select(data, sample, seed);
        var verdicts = supplied ?? Data.FormAuditor.RunOracle(repository, rows);
        var adjudicationFile = options.TryGetValue("adjudications", out var aj) ? aj : Path.Join(repository, Data.FormAuditor.AdjudicationsFile.Replace('/', Path.DirectorySeparatorChar));
        var result = Data.FormAuditor.Evaluate(rows, population, verdicts, Data.FormAuditor.ReadAdjudications(adjudicationFile), maxError);
        var text = Data.FormAuditor.Render(result, sample, seed, maxError);
        output.Write(text);
        if (options.TryGetValue("output", out var report))
        {
            File.WriteAllText(report, text, new System.Text.UTF8Encoding(false));
        }

        if (!result.Passed)
        {
            error.WriteLine($"The forms audit failed: a generated stratum has an error rate of {maxError:P2} or more, or no checked row.");
            return 1;
        }

        return 0;
    }

    private static int RunImportWn30Counts(Dictionary<string, string> options, string repository, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var sha = options.TryGetValue("sha256", out var s) ? s : Data.OewnImporter.PinnedCountsSha256;
        var result = Data.OewnImporter.ImportCounts(Require(options, InputOption), data, string.Equals(sha, "none", StringComparison.OrdinalIgnoreCase) ? null : sha);
        output.WriteLine(Data.OewnImporter.Describe(result));
        return 0;
    }

    private static int RunLabelSenses(Dictionary<string, string> options, string repository, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        output.WriteLine(Data.SenseLabelGenerator.Run(data).Render());
        return 0;
    }

    private static int RunSeedInventory(Dictionary<string, string> options, string repository, string corpus, TextWriter output)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var curation = options.TryGetValue("curation", out var c) ? Path.GetFullPath(c) : Path.Join(corpus, "curation");
        output.WriteLine(Data.InventorySeeder.Run(data, Directory.Exists(curation) ? curation : null).Render());
        return 0;
    }

    private static int RunValidateData(Dictionary<string, string> options, string repository, TextWriter output, TextWriter error)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Join(repository, "data");
        var report = Data.DataValidator.Validate(data, options.TryGetValue("baseline", out var b) ? Path.GetFullPath(b) : null);
        foreach (var violation in report.Violations)
        {
            error.WriteLine(violation);
        }

        if (!report.IsValid)
        {
            error.WriteLine($"{report.TotalViolations.ToString(CultureInfo.InvariantCulture)} violation(s) in {data}.");
            return 1;
        }

        output.WriteLine($"{data} is valid: {string.Join(", ", report.RowCounts.Select(p => $"{p.Key} {p.Value.ToString(CultureInfo.InvariantCulture)}"))}.");
        return 0;
    }

    /// <summary>
    ///     Parses <c>--name value</c> pairs; the switches <c>--no-cache</c> and <c>--recompute-mean</c> need no value.
    /// </summary>
    /// <param name="args">The arguments after the command.</param>
    /// <returns>The options by name.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is malformed or an option is unknown.</exception>
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < args.Length)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'. {Usage}");
            }

            var name = args[i][2..];
            if (FlagOptions.Contains(name))
            {
                var explicitValue = i + 1 < args.Length && args[i + 1] is "true" or "false";
                options[name] = explicitValue ? args[i + 1] : "true";
                i += explicitValue ? 2 : 1;
                continue;
            }

            if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'. {Usage}");
            }

            if (!KnownOptions.Contains(name))
            {
                throw new ArgumentException($"Unknown option '{args[i]}'. {Usage}");
            }

            options[name] = args[i + 1];
            i += 2;
        }

        return options;
    }

    /// <summary>
    ///     Gets a required option.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value.</returns>
    /// <exception cref="ArgumentException">Thrown when the option is missing.</exception>
    private static string Require(Dictionary<string, string> options, string name) =>
        options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"The option --{name} is required. {Usage}");
}
