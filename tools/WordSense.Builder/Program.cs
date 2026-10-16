using System.Text;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.MiniLM;

namespace WordSense.Builder;

/// <summary>
///     Entry point of the build-time tool that trains the embedded classifier table.
/// </summary>
/// <remarks>
///     Commands: <c>build --model DIR [--model-id ID] --corpus DIR --output FILE --weights FILE [--report FILE]</c>
///     trains the weights of the model <c>DIR/ID</c> and writes the lexicon table and the weights table;
///     the analysis commands also take <c>--model DIR [--model-id ID]</c>;
///     <c>import-batches --source DIR --corpus DIR [--max N]</c> validates and imports sentence batches;
///     <c>validate-pos --corpus DIR</c> checks the part-of-speech and pronunciation rules of every lemma;
///     <c>generate-appendix --corpus DIR --output FILE</c> writes the senses appendix of the user guide;
///     <c>import-moby --input FILE [--corrections FILE] [--classes FILE] [--output FILE]</c> derives the dictionary
///     from the Moby list; <c>lexicon-eval --table FILE --ud DIR [--gated FILE] [--output FILE] [--enforce]</c>
///     evaluates the dictionary of a table, and <c>build</c> also takes <c>--lexicon FILE|none</c>. The remaining
///     commands (<c>real-gap</c>, <c>real-judge-export</c>, <c>real-judge-adjudicate</c>, <c>real-judge-merge</c>,
///     <c>real-import</c>, <c>real-gap-import</c>, <c>real-authored-import</c>, <c>nounverb-import</c>,
///     <c>nounverb-gate</c>, <c>real-eval-import</c>, <c>real-eval</c>, <c>validate-provenance</c>,
///     <c>learning-curve</c>, <c>error-diagnosis</c> and <c>weak-lemmas</c>) prepare and evaluate the corpus; the
///     usage message printed for no arguments lists every command, and the builder design document describes them.
/// </remarks>
public static class Program
{
    /// <summary>
    ///     Runs the tool.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <returns>0 on success, 1 on a usage or data error.</returns>
    public static int Main(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    ///     Runs the tool with explicit output writers.
    /// </summary>
    /// <param name="args">The command line.</param>
    /// <param name="output">Receives normal output.</param>
    /// <param name="error">Receives error output.</param>
    /// <returns>0 on success, 1 on a usage or data error.</returns>
    internal static int Run(string[] args, TextWriter output, TextWriter error)
    {
        try
        {
            if (args.Length == 0)
            {
                error.WriteLine(
                    "Usage: WordSense.Builder <command> [--name value ...]. Commands: build, import-batches, " +
                    "validate-pos, " +
                    "real-gap, real-judge-export, real-judge-adjudicate, real-judge-merge, real-import, real-gap-import, real-authored-import, nounverb-import, nounverb-gate, " +
                    "real-eval-import, real-eval, validate-provenance, learning-curve, error-diagnosis, weak-lemmas, generate-appendix, " +
                    "import-moby, lexicon-eval.");
                return 1;
            }

            var options = ParseOptions(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "build":
                    return RunBuild(options, output);
                case "import-batches":
                    return RunImport(options, output);
                case "validate-pos":
                    return RunValidatePos(options, output);
                case "real-gap":
                    output.Write(RealPlan.Gap(Require(options, "corpus")));
                    return 0;
                case "real-judge-export":
                    return RunRealJudgeExport(options, output);
                case "real-judge-adjudicate":
                    output.WriteLine($"{RealJudge.PrepareAdjudication(Require(options, "corpus"), Require(options, "work"), IntOption(options, "size", 60))} rows need adjudication.");
                    return 0;
                case "real-judge-merge":
                    return RunRealJudgeMerge(options, output);
                case "real-import":
                    output.Write(RealImport.Import(Require(options, "corpus"), ReadCandidateFiles(Require(options, "candidates")), RealJudge.ReadJudgements(Require(options, "audit").Split(','))));
                    return 0;
                case "real-gap-import":
                    output.Write(RealGap.Import(Require(options, "corpus"), Require(options, "name"), ReadCandidateFiles(Require(options, "candidates")), RealJudge.ReadJudgements(Require(options, "audit").Split(','))));
                    return 0;
                case "real-authored-import":
                    output.Write(RealAuthored.Import(Require(options, "corpus"), Require(options, "name"), Require(options, "anchors"), Require(options, "holdout")));
                    return 0;
                case "nounverb-import":
                    return RunNounVerbImport(options, output);
                case "nounverb-gate":
                    return RunNounVerbGate(options, output);
                case "real-eval-import":
                    var perSense = IntOption(options, "per-sense", 12);
                    output.Write(RealEval.ImportEval(
                        Require(options, "corpus"),
                        ReadCandidateFiles(Require(options, "candidates")),
                        RealJudge.ReadJudgements(Require(options, "audit").Split(',')),
                        perSense));
                    return 0;
                case "real-eval":
                    using (var evalModel = OpenModel(options))
                    {
                        File.WriteAllText(
                            Require(options, "report"),
                            RealEval.RunAsync(Require(options, "corpus"), evalModel, MiniLMWordSense.LoadWeights(), CancellationToken.None).GetAwaiter().GetResult(),
                            new UTF8Encoding(false));
                    }

                    output.WriteLine("Wrote the real-text evaluation.");
                    return 0;
                case "validate-provenance":
                    return RunValidateProvenance(options, output, error);
                case "learning-curve":
                    return RunLearningCurve(options, output);
                case "error-diagnosis":
                    return RunErrorDiagnosis(options, output);
                case "weak-lemmas":
                    return RunWeakLemmas(options, output);
                case "generate-appendix":
                    return RunGenerateAppendix(options, output);
                case "import-moby":
                    return RunImportMoby(options, output);
                case "lexicon-eval":
                    return RunLexiconEval(options, output);
                default:
                    error.WriteLine($"Unknown command '{args[0]}'.");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            error.WriteLine("Error: " + ex.Message);
            return 1;
        }
    }

    /// <summary>
    ///     Parses <c>--name value</c> pairs.
    /// </summary>
    /// <param name="args">The arguments after the command.</param>
    /// <returns>The option values by name.</returns>
    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var options = new Dictionary<string, string>(StringComparer.Ordinal);
        var i = 0;
        while (i < args.Length)
        {
            // A switch without a value: it is recorded as "true"
            if (args[i] == "--enforce")
            {
                options["enforce"] = "true";
                i++;
                continue;
            }

            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'.");
            }

            // An option name where a value is expected is a usage error, for example '--output --enforce'
            if (args[i + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option {args[i]} needs a value but found the option '{args[i + 1]}'.");
            }

            options[args[i][2..]] = args[i + 1];
            i += 2;
        }

        return options;
    }

    /// <summary>
    ///     Reads a required option.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="name">The option name.</param>
    /// <returns>The value.</returns>
    private static string Require(Dictionary<string, string> options, string name)
    {
        return options.TryGetValue(name, out var value) ? value : throw new ArgumentException($"Missing required option --{name}.");
    }

    /// <summary>
    ///     Reads an optional integer option, rejecting a malformed value with a clear message.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="name">The option name.</param>
    /// <param name="defaultValue">The value used when the option is absent.</param>
    /// <returns>The parsed value or the default.</returns>
    /// <exception cref="ArgumentException">Thrown when the value is not a valid integer.</exception>
    private static int IntOption(Dictionary<string, string> options, string name, int defaultValue)
    {
        if (!options.TryGetValue(name, out var text))
        {
            return defaultValue;
        }

        return int.TryParse(text, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : throw new ArgumentException($"Option --{name} must be an integer but was '{text}'.");
    }

    /// <summary>
    ///     Reads the dictionary words and the morphology rules for a build: the file of <c>--lexicon</c>, else
    ///     <c>lexicon/lexicon.tsv</c> of the corpus; <c>--lexicon none</c> builds without a dictionary. A lexicon is
    ///     always accompanied by the <c>morphology.tsv</c> next to it, because the rules it enables decide what the
    ///     shipped table may derive.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <returns>The words (null for none) and the rules.</returns>
    /// <exception cref="ArgumentException">No <c>--lexicon</c> was given and the default lexicon is missing.</exception>
    /// <exception cref="FileNotFoundException">The lexicon or its <c>morphology.tsv</c> is missing.</exception>
    internal static (List<(string Word, byte Mask)>? Words, LemmaMorphologyRules Rules) ReadLexiconSettings(Dictionary<string, string> options, string corpus)
    {
        var explicitPath = options.GetValueOrDefault("lexicon");
        if (explicitPath == "none")
        {
            return (null, LemmaMorphologyRules.None);
        }

        var path = explicitPath ?? Path.Combine(corpus, "lexicon", LexiconSource.LexiconFile);
        if (!File.Exists(path))
        {
            throw explicitPath is null
                ? new ArgumentException($"The default lexicon '{path}' was not found; pass --lexicon FILE, or --lexicon none to build without a dictionary.")
                : new FileNotFoundException($"The lexicon '{path}' was not found.", path);
        }

        var morphology = LexiconSource.ReadMorphologyIfPresent(Path.GetDirectoryName(Path.GetFullPath(path))!)
            ?? throw new FileNotFoundException(
                $"The morphology rules '{LexiconSource.MorphologyFile}' were not found next to the lexicon '{path}'.", path);
        return (LexiconSource.ReadWords(path), LexiconSource.EnabledRules(morphology));
    }

    /// <summary>
    ///     Imports the Moby list.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code.</returns>
    private static int RunImportMoby(Dictionary<string, string> options, TextWriter output)
    {
        var import = LexiconSource.Import(
            Require(options, "input"),
            options.GetValueOrDefault("output", Path.Combine("corpus", "lexicon", LexiconSource.LexiconFile)),
            options.GetValueOrDefault("corrections"),
            options.GetValueOrDefault("classes"));
        var c = import.Counts;
        output.WriteLine(
            $"Read {c.Entries} entries, {c.SingleWords} single lower-case words; dropped {c.DroppedDeterminer} determiner words, " +
            $"{c.DroppedNoCode} without a code and {c.DroppedByClassGate} by the class gate; {c.Words} words written.");
        return 0;
    }

    /// <summary>
    ///     Evaluates the dictionary of a table on the gold corpus.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code: 1 when enforcing and a class or rule is below the gate.</returns>
    private static int RunLexiconEval(Dictionary<string, string> options, TextWriter output)
    {
        var table = WordSenseTable.Read(File.ReadAllBytes(Require(options, "table")));
        var gold = LexiconEvaluation.ReadGold(Require(options, "ud"));
        var gatedPath = options.GetValueOrDefault("gated", Path.Combine("corpus", "lexicon", LexiconSource.GatedFile));
        var result = LexiconEvaluation.Evaluate(table, gold, File.Exists(gatedPath) ? LexiconSource.ReadWords(gatedPath) : []);
        var report = result.Render();
        if (options.TryGetValue("output", out var reportPath))
        {
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        }

        output.Write(report);
        var failures = result.GateFailures();
        foreach (var failure in failures)
        {
            output.WriteLine("GATE: " + failure);
        }

        return failures.Count > 0 && options.ContainsKey("enforce") ? 1 : 0;
    }

    /// <summary>
    ///     Trains and writes the table.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code.</returns>
    private static int RunBuild(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var outputPath = Require(options, "output");
        var weightsPath = Require(options, "weights");

        // The dictionary settings are checked first, so that a usage error is reported before any heavy work
        var (words, rules) = ReadLexiconSettings(options, corpus);
        using var model = OpenModel(options);
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var forms = Corpus.ReadForms(Path.Combine(corpus, "forms", "speech.tsv"));
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var posErrors = inventory.ValidateRules();
        if (posErrors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, posErrors));
        }

        output.WriteLine($"Corpus: {anchors.Count} anchors, {holdout.Count} holdout sentences, {inventory.Rows.Count()} senses of {inventory.LemmaCount} lemmas.");

        var threads = Math.Max(1, Environment.ProcessorCount / 2);
        var result = TableBuilder.Build(model, anchors, holdout, forms, inventory, new TableBuilderOptions(Parallelism: threads), words, rules);

        var table = result.Table.ToArray();
        var weights = result.Weights.ToArray();
        var path = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, table);
        var fullWeightsPath = Path.GetFullPath(weightsPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullWeightsPath)!);
        File.WriteAllBytes(fullWeightsPath, weights);

        var report = result.Report.Render();
        output.Write(report);
        output.WriteLine($"Dictionary: {result.DictionaryWords} words embedded, {result.ShadowedWords} dropped because a trained lemma or form spells them; morphology rules: {rules}.");
        output.WriteLine($"Wrote {table.Length} bytes for {result.Table.ContextLemmas.Count} context lemmas and {result.DictionaryWords} dictionary words to {path} (lexicon SHA-256 {result.Table.Sha256}).");
        output.WriteLine($"Wrote {weights.Length} bytes of weights for model {result.Weights.ModelId} (model SHA-256 {result.Weights.ModelSha256}) to {fullWeightsPath}.");
        if (options.TryGetValue("report", out var reportPath))
        {
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        }

        return 0;
    }

    /// <summary>
    ///     Opens the encoder model named by <c>--model-id</c> (default <c>all-MiniLM-L6-v2-int8</c>) in the folder
    ///     <c>{--model}/{model-id}</c>. Only models that have a package can be opened.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <returns>The model, which the caller disposes.</returns>
    /// <exception cref="ArgumentException">Thrown when the model id is not a known model.</exception>
    internal static MiniLMWordSenseModel OpenModel(Dictionary<string, string> options)
    {
        var root = Require(options, "model");
        var id = options.GetValueOrDefault("model-id", MiniLMWordSense.ModelId);
        if (!string.Equals(id, MiniLMWordSense.ModelId, StringComparison.Ordinal))
        {
            throw new ArgumentException($"Unknown model id '{id}'; the known model is '{MiniLMWordSense.ModelId}'.");
        }

        var location = WordSenseModelLocation.FromDirectory(Path.Combine(root, id));
        return new MiniLMWordSenseModel(location.ModelPath, location.VocabularyPath, 256, 1);
    }

    /// <summary>
    ///     Loads the corpus and encodes every sentence for the analysis commands.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <returns>The featurized corpus.</returns>
    private static AnalysisData LoadAnalysis(Dictionary<string, string> options)
    {
        var corpus = Require(options, "corpus");
        var filter = options.TryGetValue("lemmas", out var list)
            ? list.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal)
            : null;
        using var model = OpenModel(options);
        return AnalysisData.Load(model, corpus, filter, new TableBuilderOptions(Parallelism: Math.Max(1, Environment.ProcessorCount / 2)));
    }

    private static int RunLearningCurve(Dictionary<string, string> options, TextWriter output)
    {
        var subsets = IntOption(options, "subsets", LearningCurve.DefaultSubsets);
        var seed = IntOption(options, "seed", LearningCurve.DefaultSeed);
        var outDirectory = Require(options, "out");
        var lemmas = LearningCurve.RunAndWrite(LoadAnalysis(options), outDirectory, subsets, seed);
        output.WriteLine($"Wrote the learning curves of {lemmas} lemmas to {outDirectory}.");
        return 0;
    }

    private static int RunErrorDiagnosis(Dictionary<string, string> options, TextWriter output)
    {
        var outDirectory = Require(options, "out");
        var result = ErrorDiagnosis.RunAndWrite(LoadAnalysis(options), outDirectory);
        output.WriteLine($"Wrote the diagnosis of {result.Rows.Count} errors to {outDirectory}.");
        return 0;
    }

    private static int RunGenerateAppendix(Dictionary<string, string> options, TextWriter output)
    {
        var target = Require(options, "output");
        var lemmas = AppendixGenerator.Write(Require(options, "corpus"), target);
        output.WriteLine($"Wrote the appendix of {lemmas} lemmas to {target}.");
        return 0;
    }

    private static int RunWeakLemmas(Dictionary<string, string> options, TextWriter output)
    {
        var directory = Require(options, "analysis");
        var ranked = WeakLemmas.RunAndWrite(directory);
        if (options.TryGetValue("report", out var reportPath))
        {
            File.WriteAllText(reportPath, WeakLemmas.RenderReport(directory), new UTF8Encoding(false));
        }

        output.WriteLine($"Ranked {ranked.Count} weak lemmas in {directory}.");
        return 0;
    }

    private static int RunNounVerbImport(Dictionary<string, string> options, TextWriter output)
    {
        var defaults = new NounVerbOptions();
        var settings = new NounVerbOptions(
            IntOption(options, "min-anchors", defaults.MinAnchors),
            IntOption(options, "min-holdout", defaults.MinHoldout),
            IntOption(options, "max-anchors", defaults.MaxAnchors),
            IntOption(options, "max-holdout", defaults.MaxHoldout));
        output.Write(NounVerbImport.Import(
            Require(options, "corpus"),
            Require(options, "name"),
            Require(options, "sentences").Split(','),
            Require(options, "provenance").Split(','),
            Require(options, "exclusions"),
            options.GetValueOrDefault("forms"),
            settings));
        return 0;
    }

    private static int RunNounVerbGate(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var apply = BoolOption(options, "apply");
        var defaults = new GatePolicy();
        var policy = new GatePolicy(
            DoubleOption(options, "min-best", defaults.MinBestSense, 0, 1),
            DoubleOption(options, "min-sense", defaults.MinSenseAnswered, 0, 1),
            DoubleOption(options, "min-coverage", defaults.MinCoverage, 0, 1));
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var forms = Corpus.ReadForms(Path.Combine(corpus, "forms", "speech.tsv"));
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        using var model = OpenModel(options);
        var result = TableBuilder.Build(model, anchors, holdout, forms, inventory, new TableBuilderOptions(Parallelism: Math.Max(1, Environment.ProcessorCount / 2)));
        var results = NounVerbGate.Evaluate(result.Report.Lemmas, NounVerbGate.ReadScope(corpus), policy);
        var failed = results.Where(r => !r.Passed).ToList();
        output.WriteLine($"Noun/verb lemmas: {results.Count}; passed {results.Count - failed.Count}; failed {failed.Count}.");
        foreach (var f in failed)
        {
            output.WriteLine($"  {f.Lemma}: {f.Failure}");
        }

        if (options.TryGetValue("results", out var resultsPath))
        {
            File.WriteAllText(resultsPath, NounVerbGate.RenderTsv(results), new UTF8Encoding(false));
        }

        if (options.TryGetValue("report", out var reportPath))
        {
            File.WriteAllText(reportPath, NounVerbGate.RenderMarkdown(results), new UTF8Encoding(false));
        }

        if (apply)
        {
            NounVerbGate.Drop(corpus, failed);
            output.WriteLine($"Moved {failed.Count} lemmas out of the corpus into audit/{NounVerbGate.DroppedFile}.");
        }

        return 0;
    }

    private static bool BoolOption(Dictionary<string, string> options, string name)
    {
        if (!options.TryGetValue(name, out var text))
        {
            return false;
        }

        return text switch
        {
            "true" => true,
            "false" => false,
            _ => throw new ArgumentException($"Option --{name} must be true or false.")
        };
    }

    private static double DoubleOption(Dictionary<string, string> options, string name, double fallback, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        if (!options.TryGetValue(name, out var text))
        {
            return fallback;
        }

        var value = double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : throw new ArgumentException($"Option --{name} must be a number.");
        return value >= min && value <= max
            ? value
            : throw new ArgumentException($"Option --{name} must be between {min} and {max}.");
    }

    private static List<Candidate> ReadCandidateFiles(string list) => list.Split(',').SelectMany(RealJudge.ReadCandidates).ToList();

    private static int RunRealJudgeExport(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var gapPlan = options.GetValueOrDefault("gap-plan");
        if (gapPlan is not null)
        {
            RealGap.ValidateName(gapPlan);
        }

        var candidates = options.ContainsKey("candidates") ? ReadCandidateFiles(options["candidates"]) : [];
        if (gapPlan is null && options.GetValueOrDefault("undecided", "false") == "true")
        {
            var (_, undecided, _) = RealPlan.ReadPool(corpus, RealPlan.ReadPlan(corpus));
            candidates.AddRange(undecided.Select(r => new Candidate(PosAudit.RowId(r.Lemma, r.Sentence), r.Lemma, r.Sentence, "llm-corpus", "MIT (project-authored)", "dropped-rows.tsv", "-", "project (LLM-written)", r.Split)));
        }

        var size = IntOption(options, "size", 100);
        var (rows, chunks) = RealJudge.Export(corpus, candidates, Require(options, "work"), size, gapPlan);
        output.WriteLine($"Exported {rows} rows in {chunks} chunks.");
        return 0;
    }

    private static int RunRealJudgeMerge(Dictionary<string, string> options, TextWriter output)
    {
        var results = RealJudge.Merge(Require(options, "corpus"), Require(options, "work"));
        RealJudge.WriteJudgements(Require(options, "output"), results);
        output.WriteLine($"Merged {results.Count} rows ({results.Count(r => r.Resolution == "agree")} agree, {results.Count(r => r.Resolution == "adjudicated")} adjudicated, {results.Count(r => r.Resolution == "undecided")} undecided, {results.Count(r => r.Final == RealJudge.None)} none).");
        return 0;
    }

    private static int RunValidateProvenance(Dictionary<string, string> options, TextWriter output, TextWriter error)
    {
        var errors = RealImport.Validate(Require(options, "corpus"));
        foreach (var message in errors)
        {
            error.WriteLine(message);
        }

        output.WriteLine(errors.Count == 0 ? "The provenance sidecar is complete and valid." : $"{errors.Count} provenance problems.");
        return errors.Count == 0 ? 0 : 1;
    }

    private static int RunValidatePos(Dictionary<string, string> options, TextWriter output)
    {
        var inventory = SenseInventory.ReadDirectory(Path.Combine(Require(options, "corpus"), "senses"));
        var errors = inventory.ValidateRules();
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        output.WriteLine($"{inventory.Rows.Count()} senses of {inventory.LemmaCount} lemmas satisfy the part-of-speech and pronunciation rules.");
        return 0;
    }

    /// <summary>
    ///     Imports sentence batches.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code: 1 when any batch is invalid.</returns>
    private static int RunImport(Dictionary<string, string> options, TextWriter output)
    {
        var max = IntOption(options, "max", 8);
        var results = BatchImporter.Import(Require(options, "source"), Require(options, "corpus"), max);
        foreach (var result in results)
        {
            output.WriteLine($"batch{result.Batch}: {result.Status} ({result.Detail})");
            foreach (var note in result.Notes ?? [])
            {
                output.WriteLine("  " + note);
            }
        }

        return results.Any(r => r.Status == "Invalid") ? 1 : 0;
    }
}
