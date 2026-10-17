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
///     <item><description><c>words [--corpus DIR] [--output FILE]</c> writes the word table of the core package.</description></item>
///     <item><description><c>weights &lt;modelId&gt; [--corpus DIR] [--repository DIR]</c> trains the weights of an ONNX model (minilm, bert-base-cased) and writes them to its package.</description></item>
///     <item><description><c>evaluate &lt;modelId&gt; [--corpus DIR] [--repository DIR] [--output FILE]</c> measures a shipped model (minilm, bert-base-cased, basic).</description></item>
///     <item><description><c>basic-precision [--corpus DIR] [--output FILE]</c> measures the precision of each rule of the basic model on the training anchors.</description></item>
///     <item><description><c>appendix [--corpus DIR] [--output FILE]</c> writes the generated senses appendix of the user guide.</description></item>
///     </list>
/// </remarks>
internal static class Program
{
    private const string Usage = "Usage: WordSense.Builder <command> [--name value ...]. Commands: words, weights <modelId>, evaluate <modelId>, basic-precision, appendix.";

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
                case "words":
                    var wordsOutput = options.TryGetValue("output", out var w) ? w : Path.Combine(repository, WordsBuilder.DefaultOutput);
                    var count = WordsBuilder.Write(corpus, wordsOutput);
                    output.WriteLine($"Wrote {count.ToString(CultureInfo.InvariantCulture)} words to {wordsOutput}.");
                    return 0;
                case "weights":
                    await ModelTrainer.TrainAsync(repository, corpus, ModelCatalog.Find(Require(options, "model")), output, cancellationToken).ConfigureAwait(false);
                    return 0;
                case "basic-precision":
                    var (rules, anchorCount) = BasicPrecision.Measure(corpus);
                    var precision = BasicPrecision.Render(rules, anchorCount);
                    output.Write(precision);
                    if (options.TryGetValue("output", out var precisionReport))
                    {
                        File.WriteAllText(precisionReport, precision, new System.Text.UTF8Encoding(false));
                    }

                    return 0;
                case "evaluate":
                    var result = await Evaluator.RunAsync(repository, corpus, ModelCatalog.Find(Require(options, "model")), output, cancellationToken).ConfigureAwait(false);
                    var text = result.Render();
                    output.Write(text);
                    if (options.TryGetValue("output", out var report))
                    {
                        File.WriteAllText(report, text, new System.Text.UTF8Encoding(false));
                    }

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
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static readonly HashSet<string> KnownOptions = new(["repository", "corpus", "output", "model"], StringComparer.Ordinal);

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
