using System.Text;
using DemaConsulting.WordSense;

namespace WordSense.Builder;

/// <summary>
///     Entry point of the build-time tool that trains the embedded classifier table.
/// </summary>
/// <remarks>
///     Commands: <c>build --model DIR --corpus DIR --output FILE [--report FILE]</c> trains and writes the
///     table; <c>import-batches --source DIR --corpus DIR [--max N]</c> validates and imports sentence batches;
///     <c>derive-senses --corpus DIR</c> writes the sense inventory derived from the anchors.
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
                error.WriteLine("Usage: WordSense.Builder build|import-batches [options]");
                return 1;
            }

            var options = ParseOptions(args.Skip(1).ToArray());
            switch (args[0])
            {
                case "build":
                    return RunBuild(options, output);
                case "import-batches":
                    return RunImport(options, output);
                case "derive-senses":
                    return RunDerive(options, output);
                default:
                    error.WriteLine($"Unknown command '{args[0]}'.");
                    return 1;
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or ArgumentException or IOException or FileNotFoundException)
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

    /// <summary>
    ///     Writes the derived sense inventory files, one per anchor file (a one-off bootstrap command).
    /// </summary>
    /// <param name="options">The parsed options.</param>
    /// <param name="output">The output writer.</param>
    /// <returns>The exit code.</returns>
    private static int RunDerive(Dictionary<string, string> options, TextWriter output)
    {
        var corpus = Require(options, "corpus");
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var files = SenseTagDerivation.DeriveFiles(anchors);
        foreach (var (name, rows) in files)
        {
            SenseInventory.WriteFile(Path.Combine(corpus, "senses", name), rows);
            output.WriteLine($"senses/{name}: {rows.Count} senses");
        }

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
        var max = options.TryGetValue("max", out var value) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) : 8;
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
