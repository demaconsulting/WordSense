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
///     <item><description><c>bootstrap-forms [--data DIR] [--corpus DIR]</c> derives the form tables of the data folder from the legacy corpus once.</description></item>
///     <item><description><c>import-moby --input FILE [--corpus DIR] [--output FILE]</c> imports the pinned Moby Part-of-Speech II list into the dictionary word list of the corpus.</description></item>
///     <item><description><c>weights &lt;modelId&gt; [--corpus DIR] [--repository DIR]</c> trains the weights of an ONNX model (minilm, bert-base-cased) and writes them to its package.</description></item>
///     <item><description><c>evaluate &lt;modelId&gt; [--corpus DIR] [--repository DIR] [--output FILE] [--table FILE] [--outcomes FILE]</c> measures a shipped model (minilm, bert-base-cased, basic); <c>--table</c> updates the row of the model in the model comparison table file; <c>--outcomes</c> writes the answer for every sentence.</description></item>
///     <item><description><c>ud-counts [--ud DIR] [--corpus DIR] [--output DIR]</c> downloads and verifies the pinned treebank files and writes the count files of <c>corpus/ud</c>.</description></item>
///     <item><description><c>train-basic [--corpus DIR] [--repository DIR] [--ud DIR] [--refresh-ud true] [--output FILE]</c> retrains the basic model completely and writes its table; it regenerates the treebank counts first when they are missing or <c>--refresh-ud true</c> is given.</description></item>
///     <item><description><c>tune-basic [--corpus DIR] [--repository DIR] [--ud DIR]</c> scores the hyper-parameter grid on the treebank development file and the anchor dev sentences; it writes nothing.</description></item>
///     <item><description><c>ud-irregular [--ud DIR] [--corpus DIR] [--output FILE]</c> derives the irregular inflected forms of the dictionary words from the pinned treebank training and development files.</description></item>
///     <item><description><c>coverage --text DIR [--corpus DIR] [--top N] [--output FILE]</c> reports how many of the running words of the text files of a directory the word table knows and lists the most frequent unknown words, split into name-like and other words.</description></item>
///     <item><description><c>appendix [--corpus DIR] [--output FILE]</c> writes the generated senses appendix of the user guide.</description></item>
///     <item><description><c>import-oewn --input FILE [--output DIR] [--sha256 HEX|none] [--threshold N] [--overwrite true]</c> imports the pinned Open English WordNet release into the table files of the data folder (default <c>data</c>) and refuses to replace existing tables unless <c>--overwrite true</c> is given.</description></item>
///     <item><description><c>validate-data [--data DIR]</c> validates the table files of the data folder against its <c>schema.yaml</c>; it exits with 1 and lists the violations when the data is not valid.</description></item>
///     </list>
/// </remarks>
internal static class Program
{
    private const string Usage = "Usage: WordSense.Builder <command> [--name value ...]. Commands: compile-image, bootstrap-forms, import-moby, weights <modelId>, evaluate <modelId>, ud-counts, ud-irregular, coverage, train-basic, tune-basic, appendix, import-oewn, validate-data.";

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
            var corpus = options.TryGetValue("corpus", out var c) ? Path.GetFullPath(c) : Path.Combine(repository, "corpus");
            switch (args[0])
            {
                case "compile-image":
                    var dataFolder = options.TryGetValue("data", out var cd) ? Path.GetFullPath(cd) : Path.Combine(repository, "data");
                    var imageOutput = options.TryGetValue("output", out var imo) ? imo : Path.Combine(repository, Data.ImageCompiler.DefaultOutput);
                    var image = Data.ImageCompiler.Compile(dataFolder, imageOutput);
                    output.WriteLine($"Wrote the word image to {imageOutput}: {image.Spellings.ToString(CultureInfo.InvariantCulture)} spellings, {image.Analyses.ToString(CultureInfo.InvariantCulture)} analyses, {image.Entries.ToString(CultureInfo.InvariantCulture)} entries, {image.Bytes.ToString(CultureInfo.InvariantCulture)} bytes.");
                    return 0;
                case "import-moby":
                    var mobyInput = Require(options, "input");
                    var lexiconOutput = options.TryGetValue("output", out var lo) ? lo : Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile);
                    var imported = LexiconImporter.Import(mobyInput, lexiconOutput);
                    output.WriteLine(LexiconImporter.Render(imported));
                    return 0;
                case "weights":
                    await ModelTrainer.TrainAsync(repository, corpus, ModelCatalog.Find(Require(options, "model")), output, cancellationToken).ConfigureAwait(false);
                    return 0;
                case "ud-counts":
                    var udDirectory = options.TryGetValue("ud", out var ud) ? Path.GetFullPath(ud) : BasicModelBuilder.DefaultUdCache(repository);
                    var countsOutput = options.TryGetValue("output", out var co) ? Path.GetFullPath(co) : Path.Combine(corpus, "ud");
                    output.WriteLine(await BasicModelBuilder.WriteUdCountsAsync(udDirectory, countsOutput, cancellationToken).ConfigureAwait(false));
                    return 0;
                case "train-basic":
                    var cache = options.TryGetValue("ud", out var ud2) ? Path.GetFullPath(ud2) : BasicModelBuilder.DefaultUdCache(repository);
                    var refresh = options.TryGetValue("refresh-ud", out var r) && string.Equals(r, "true", StringComparison.OrdinalIgnoreCase);
                    var tablePath = options.TryGetValue("output", out var to) ? to : BasicModelBuilder.DefaultOutput;
                    await BasicModelBuilder.TrainAsync(repository, corpus, cache, refresh, tablePath, output, cancellationToken).ConfigureAwait(false);
                    return 0;
                case "tune-basic":
                    var tuneCache = options.TryGetValue("ud", out var ud3) ? Path.GetFullPath(ud3) : BasicModelBuilder.DefaultUdCache(repository);
                    output.Write(await BasicTuner.RunAsync(corpus, tuneCache, output, cancellationToken).ConfigureAwait(false));
                    return 0;
                case "evaluate":
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
                case "ud-irregular":
                    var irregularCache = options.TryGetValue("ud", out var ud4) ? Path.GetFullPath(ud4) : BasicModelBuilder.DefaultUdCache(repository);
                    var irregularOutput = options.TryGetValue("output", out var io) ? io : Path.Combine(corpus, "lexicon", IrregularForms.DerivedFile);
                    output.WriteLine(IrregularForms.Derive(irregularCache, corpus, irregularOutput));
                    return 0;
                case "coverage":
                    var textDirectory = Path.GetFullPath(Require(options, "text"));
                    var topCount = options.TryGetValue("top", out var tp) ? int.Parse(tp, CultureInfo.InvariantCulture) : 100;
                    var table = DemaConsulting.WordSense.WordLexicon.Current;
                    var coverage = CoverageReport.Read(textDirectory, w => table.TryLookup(w, out _)).Render(topCount);
                    output.Write(coverage);
                    if (options.TryGetValue("output", out var coverageFile))
                    {
                        File.WriteAllText(coverageFile, coverage, new System.Text.UTF8Encoding(false));
                    }

                    return 0;
                case "import-oewn":
                    return RunImportOewn(options, repository, output);
                case "validate-data":
                    return RunValidateData(options, repository, output, error);
                case "bootstrap-forms":
                    var bootData = options.TryGetValue("data", out var bd) ? Path.GetFullPath(bd) : Path.Combine(repository, "data");
                    var boot = Data.FormBootstrap.Run(bootData, corpus);
                    output.WriteLine($"Bootstrapped {boot.Spellings.ToString(CultureInfo.InvariantCulture)} spellings: {boot.Forms.ToString(CultureInfo.InvariantCulture)} analyses ({boot.Analyses.ToString(CultureInfo.InvariantCulture)} legacy answers), {boot.ScopeRows.ToString(CultureInfo.InvariantCulture)} scope rows, {boot.NewEntries.ToString(CultureInfo.InvariantCulture)} new entries.");
                    return 0;
                case "appendix":
                    var appendix = options.TryGetValue("output", out var a) ? a : Path.Combine(repository, "docs", "user_guide", "appendix-senses.md");
                    var lemmas = AppendixGenerator.Write(corpus, appendix);
                    output.WriteLine($"Wrote the appendix of {lemmas.ToString(CultureInfo.InvariantCulture)} lemmas to {appendix}.");
                    return 0;
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

    private static readonly HashSet<string> KnownOptions = new(["repository", "corpus", "output", "model", "ud", "refresh-ud", "table", "input", "text", "top", "data", "sha256", "threshold", "overwrite", "outcomes"], StringComparer.Ordinal);

    private static int RunImportOewn(Dictionary<string, string> options, string repository, TextWriter output)
    {
        var data = options.TryGetValue("output", out var o) ? Path.GetFullPath(o) : Path.Combine(repository, "data");
        var sha = options.TryGetValue("sha256", out var s) ? s : Data.OewnImporter.PinnedSha256;
        var threshold = options.TryGetValue("threshold", out var t) ? int.Parse(t, CultureInfo.InvariantCulture) : Data.OewnImporter.DefaultSplitThreshold;
        var overwrite = options.TryGetValue("overwrite", out var w) && string.Equals(w, "true", StringComparison.OrdinalIgnoreCase);
        var result = Data.OewnImporter.Import(Require(options, "input"), data, Path.Combine(data, "schema.yaml"), string.Equals(sha, "none", StringComparison.OrdinalIgnoreCase) ? null : sha, threshold, overwrite);
        output.Write(result.Render());
        return 0;
    }

    private static int RunValidateData(Dictionary<string, string> options, string repository, TextWriter output, TextWriter error)
    {
        var data = options.TryGetValue("data", out var d) ? Path.GetFullPath(d) : Path.Combine(repository, "data");
        var report = Data.DataValidator.Validate(data);
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
    ///     Parses <c>--name value</c> pairs.
    /// </summary>
    /// <param name="args">The arguments after the command.</param>
    /// <returns>The options by name.</returns>
    /// <exception cref="ArgumentException">Thrown when an argument is malformed or an option is unknown.</exception>
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'. {Usage}");
            }

            if (!KnownOptions.Contains(args[i][2..]))
            {
                throw new ArgumentException($"Unknown option '{args[i]}'. {Usage}");
            }

            options[args[i][2..]] = args[i + 1];
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
