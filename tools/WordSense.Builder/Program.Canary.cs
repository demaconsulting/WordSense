using System.Globalization;
using System.Runtime.InteropServices;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The <c>canary</c> command of the builder.
/// </summary>
internal static partial class Program
{
    private const string DefaultCanaryModel = "bert-base-cased";

    /// <summary>
    ///     Runs <c>canary --write FILE --sentences FILE</c> or <c>canary --check FILE</c>.
    /// </summary>
    /// <param name="options">The options of the command.</param>
    /// <param name="repository">The repository root.</param>
    /// <param name="output">The writer of the report.</param>
    /// <param name="cancellationToken">A token that cancels the operation.</param>
    /// <returns>0 when the file was written or the check passed, 1 when the check failed.</returns>
    /// <exception cref="ArgumentException">Thrown when the options do not name exactly one of <c>--write</c> and <c>--check</c>, or a value is invalid.</exception>
    private static async Task<int> RunCanaryAsync(Dictionary<string, string> options, string repository, TextWriter output, CancellationToken cancellationToken)
    {
        var hasWrite = options.TryGetValue("write", out var writePath);
        var hasCheck = options.TryGetValue("check", out var checkPath);
        if (hasWrite == hasCheck)
        {
            throw new ArgumentException($"The canary command needs exactly one of --write FILE and --check FILE. {Usage}");
        }

        int? armTolerance = null;
        if (options.TryGetValue("arm-tolerance", out var toleranceText))
        {
            // NumberStyles.None refuses a sign, so a negative number is refused with the other malformed values
            armTolerance = int.TryParse(toleranceText, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0
                ? parsed
                : throw new ArgumentException($"The option --arm-tolerance must be a number of words that is not negative, not '{toleranceText}'. {Usage}");
        }

        return hasWrite
            ? await WriteCanaryAsync(Path.GetFullPath(writePath!), Require(options, "sentences"), options.GetValueOrDefault("model", DefaultCanaryModel), armTolerance ?? Canary.DefaultArmTolerance, repository, output, cancellationToken).ConfigureAwait(false)
            : await CheckCanaryAsync(Path.GetFullPath(checkPath!), armTolerance, repository, output, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<int> WriteCanaryAsync(string file, string sentencesPath, string modelName, int armTolerance, string repository, TextWriter output, CancellationToken cancellationToken)
    {
        var info = ModelCatalog.Find(modelName);
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' has no encoder, so it has no CPU canary.");
        }

        var sentences = Canary.ReadSentences(Path.GetFullPath(sentencesPath));
        using var model = ModelCatalog.Open(info, repository);
        await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        var entries = await Canary.AnswerAsync(model, sentences, cancellationToken).ConfigureAwait(false);
        var header = new List<KeyValuePair<string, string>>
        {
            new("model", info.Name),
            new("encoder-sha256", ModelTrainer.EncoderShaOf(ModelCatalog.EncoderOf(model).Spec)),
            new("arm-tolerance", armTolerance.ToString(CultureInfo.InvariantCulture)),
            new("written-on", Canary.Machine()),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, Canary.Render(header, entries), new System.Text.UTF8Encoding(false));
        output.WriteLine($"canary: wrote the answers of {info.Name} to {file}: {entries.Count} sentences, {entries.Sum(e => e.Words.Length)} words, on {Canary.Machine()}.");
        return 0;
    }

    private static async Task<int> CheckCanaryAsync(string file, int? armToleranceOption, string repository, TextWriter output, CancellationToken cancellationToken)
    {
        var canary = Canary.Read(file);
        var modelName = canary.Header.GetValueOrDefault("model", DefaultCanaryModel);
        var info = ModelCatalog.Find(modelName);
        if (!info.IsEncoder)
        {
            throw new ArgumentException($"The model '{info.Name}' has no encoder, so it has no CPU canary.");
        }

        var armTolerance = Canary.ResolveArmTolerance(canary.Header, armToleranceOption);
        using var model = ModelCatalog.Open(info, repository);
        await ModelCatalog.EnsureAsync(model, cancellationToken).ConfigureAwait(false);
        var sha = ModelTrainer.EncoderShaOf(ModelCatalog.EncoderOf(model).Spec);
        if (canary.Header.TryGetValue("encoder-sha256", out var written) && !string.Equals(written, sha, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"The canary file was written for the encoder file {written}, not for {sha}; write it again with 'canary --write'.");
        }

        var actual = await Canary.AnswerAsync(model, [.. canary.Entries.Select(e => new CanarySentence(e.Id, e.Words))], cancellationToken).ConfigureAwait(false);
        var flips = Canary.Compare(canary.Entries, actual);
        var words = canary.Entries.Sum(e => e.Words.Length);
        var verdict = Canary.Judge(flips.Count, RuntimeInformation.ProcessArchitecture, armTolerance);
        foreach (var flip in flips)
        {
            output.WriteLine($"canary: {flip.Id} word {flip.Index} '{flip.Word}': expected {flip.Expected}, got {flip.Actual}");
        }

        var status = verdict.Passed ? "passed" : "FAILED";
        output.WriteLine(verdict.Applicable
            ? $"canary: {flips.Count} of {words} words differ on {Canary.Machine()}; at most {verdict.Limit} may: {status}."
            : $"canary: {flips.Count} of {words} words differ on {Canary.Machine()}; this architecture is not judged: skipped.");
        return verdict.Passed ? 0 : 1;
    }
}
