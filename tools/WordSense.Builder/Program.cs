using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Entry point of the build-time tool that trains the embedded classifier table.
/// </summary>
/// <remarks>
///     Commands: <c>build --model DIR --corpus DIR --output FILE [--report FILE]</c> trains and writes the
///     table; <c>import-batches --source DIR --corpus DIR [--max N]</c> validates and imports sentence batches;
///     <c>validate-pos --corpus DIR</c> checks that every sense has exactly one part of speech; the
///     <c>audit-*</c> commands and <c>apply-relabel</c> drive the part-of-speech audit and relabel.
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
                    "audit-export, audit-adjudicate, audit-merge, audit-report, apply-relabel, validate-pos, " +
                    "real-gap, real-judge-export, real-judge-adjudicate, real-judge-merge, real-import, " +
                    "real-eval-import, real-eval, validate-provenance.");
                return 1;
            }

            var options = ParseOptions(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "build":
                    return RunBuild(options, output);
                case "import-batches":
                    return RunImport(options, output);
                case "audit-export":
                    return RunAuditExport(options, output);
                case "audit-adjudicate":
                    return RunAuditAdjudicate(options, output);
                case "audit-merge":
                    return RunAuditMerge(options, output);
                case "audit-report":
                    return RunAuditReport(options, output);
                case "apply-relabel":
                    return RunApplyRelabel(options, output);
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
                case "real-eval-import":
                    var perSense = IntOption(options, "per-sense", 12);
                    output.Write(RealEval.ImportEval(
                        Require(options, "corpus"),
                        ReadCandidateFiles(Require(options, "candidates")),
                        RealJudge.ReadJudgements(Require(options, "audit").Split(',')),
                        perSense));
                    return 0;
                case "real-eval":
                    File.WriteAllText(Require(options, "report"), RealEval.RunAsync(Require(options, "corpus"), Require(options, "model"), CancellationToken.None).GetAwaiter().GetResult(), new UTF8Encoding(false));
                    output.WriteLine("Wrote the real-text evaluation.");
                    return 0;
                case "validate-provenance":
                    return RunValidateProvenance(options, output, error);
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
        for (var i = 0; i < args.Length; i += 2)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal) || i + 1 >= args.Length)
            {
                throw new ArgumentException($"Expected '--name value' but found '{args[i]}'.");
            }

            options[args[i][2..]] = args[i + 1];
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
    ///     Trains and writes the table.
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code.</returns>
    private static int RunBuild(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var location = WordSenseModelLocation.FromDirectory(Require(options, "model"));
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var forms = Corpus.ReadForms(Path.Combine(corpus, "forms", "speech.tsv"));
        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var posErrors = PosRules.Validate(inventory);
        if (posErrors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, posErrors));
        }

        output.WriteLine($"Corpus: {anchors.Count} anchors, {holdout.Count} holdout sentences, {inventory.Rows.Count()} senses of {inventory.LemmaCount} lemmas.");

        var threads = Math.Max(1, Environment.ProcessorCount / 2);
        using var encoder = new OnnxWordSenseEncoder(location.ModelPath, 1);
        var tokenizer = WordPieceTokenizer.FromFile(location.VocabularyPath);
        var result = TableBuilder.Build(encoder, tokenizer, anchors, holdout, forms, inventory, new TableBuilderOptions(Parallelism: threads));

        var table = result.Table.ToArray();
        var path = Path.GetFullPath(Require(options, "output"));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, table);

        var report = result.Report.Render();
        output.Write(report);
        output.WriteLine($"Wrote {table.Length} bytes for {result.Table.Lemmas.Count} lemmas to {path}.");
        if (options.TryGetValue("report", out var reportPath))
        {
            File.WriteAllText(reportPath, report, new UTF8Encoding(false));
        }

        return 0;
    }

    private static int RunAuditExport(Dictionary<string, string> options, TextWriter output)
    {
        var size = IntOption(options, "size", 200);
        var (rows, chunks) = PosAudit.Export(Require(options, "corpus"), Require(options, "work"), size);
        output.WriteLine($"Exported {rows} rows in {chunks} chunks.");
        return 0;
    }

    private static int RunAuditAdjudicate(Dictionary<string, string> options, TextWriter output)
    {
        var size = IntOption(options, "size", 120);
        var (adjudicate, probe) = PosAudit.PrepareAdjudication(Require(options, "corpus"), Require(options, "work"), size, 0.05);
        output.WriteLine($"{adjudicate} rows need adjudication; {probe} auto-accepted rows are probed blind.");
        return 0;
    }

    private static int RunAuditMerge(Dictionary<string, string> options, TextWriter output)
    {
        var results = PosAudit.Merge(Require(options, "corpus"), Require(options, "work"));
        PosAudit.WriteResults(Require(options, "output"), results);
        output.WriteLine($"Merged {results.Count} rows ({results.Count(r => r.Resolution == "agree")} agree, {results.Count(r => r.Resolution == "adjudicated")} adjudicated, {results.Count(r => r.Resolution == PosAudit.Undecided)} undecided).");
        return 0;
    }

    private static int RunAuditReport(Dictionary<string, string> options, TextWriter output)
    {
        var text = PosAuditReport.Render(PosAudit.ReadResults(Require(options, "audit")), options.GetValueOrDefault("notes"));
        File.WriteAllText(Require(options, "output"), text, new UTF8Encoding(false));
        output.WriteLine("Wrote the audit report.");
        return 0;
    }

    private static int RunApplyRelabel(Dictionary<string, string> options, TextWriter output)
    {
        output.WriteLine(Relabeler.Apply(Require(options, "corpus")));
        return 0;
    }

    private static List<Candidate> ReadCandidateFiles(string list) => list.Split(',').SelectMany(RealJudge.ReadCandidates).ToList();

    private static int RunRealJudgeExport(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var candidates = options.ContainsKey("candidates") ? ReadCandidateFiles(options["candidates"]) : [];
        if (options.GetValueOrDefault("undecided", "false") == "true")
        {
            var (_, undecided, _) = RealPlan.ReadPool(corpus, RealPlan.ReadPlan(corpus));
            candidates.AddRange(undecided.Select(r => new Candidate(PosAudit.RowId(r.Lemma, r.Sentence), r.Lemma, r.Sentence, "llm-corpus", "MIT (project-authored)", "dropped-rows.tsv", "-", "project (LLM-written)", r.Split)));
        }

        var size = IntOption(options, "size", 100);
        var (rows, chunks) = RealJudge.Export(corpus, candidates, Require(options, "work"), size);
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
        var errors = PosRules.Validate(inventory);
        if (errors.Count > 0)
        {
            throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        }

        output.WriteLine($"{inventory.Rows.Count()} senses have exactly one part of speech.");
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
