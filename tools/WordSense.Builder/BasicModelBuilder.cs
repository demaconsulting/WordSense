using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The commands that create the table of the basic model: <c>ud-counts</c> (the treebank statistics) and
///     <c>train-basic</c> (the complete retraining, the one command to run after changing anything).
/// </summary>
internal static class BasicModelBuilder
{
    /// <summary>
    ///     The path of the table below the repository root.
    /// </summary>
    public static readonly string DefaultOutput = Path.Combine("src", "DemaConsulting.WordSense.Basic", "Resources", "basic.bin");

    /// <summary>
    ///     Gets the default directory of the cached treebank files, below the repository root.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <returns>The directory.</returns>
    public static string DefaultUdCache(string repository) => Path.Combine(repository, "tools", "real-text", "cache");

    /// <summary>
    ///     Downloads and verifies the pinned treebank files and writes the count files.
    /// </summary>
    /// <param name="udCache">The directory of the treebank files.</param>
    /// <param name="outputDirectory">The directory of the count files (<c>corpus/ud</c>).</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A one-line summary.</returns>
    public static async Task<string> WriteUdCountsAsync(string udCache, string outputDirectory, CancellationToken cancellationToken)
    {
        await UdSource.EnsureAsync(udCache, cancellationToken).ConfigureAwait(false);
        var sentences = UdCounts.ReadConllu(Path.Combine(udCache, "en_ewt-ud-train.conllu"));
        var counts = UdCounts.Count(sentences);
        counts.Write(outputDirectory);
        return $"Counted {sentences.Count.ToString(CultureInfo.InvariantCulture)} sentences into {counts.Words.Count.ToString(CultureInfo.InvariantCulture)} words in {outputDirectory}.";
    }

    /// <summary>
    ///     Retrains the basic model completely and writes its table.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="udCache">The directory of the treebank files.</param>
    /// <param name="refreshUd">True to regenerate the count files from the treebank even when they exist.</param>
    /// <param name="outputPath">The table path.</param>
    /// <param name="output">Receives messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the table is written.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent.</exception>
    public static async Task TrainAsync(string repository, string corpus, string udCache, bool refreshUd, string outputPath, TextWriter output, CancellationToken cancellationToken)
    {
        var udDirectory = Path.Combine(corpus, "ud");
        var hasCounts = File.Exists(Path.Combine(udDirectory, UdCounts.NgramsFile)) && File.Exists(Path.Combine(udDirectory, UdCounts.WordsFile));
        if (refreshUd || !hasCounts)
        {
            output.WriteLine(await WriteUdCountsAsync(udCache, udDirectory, cancellationToken).ConfigureAwait(false));
        }

        var inventory = SenseInventory.ReadDirectory(Path.Combine(corpus, "senses"));
        var anchors = Corpus.ReadDirectory(Path.Combine(corpus, "anchors"));
        var holdout = Corpus.ReadDirectory(Path.Combine(corpus, "holdout"));
        var formsDirectory = Path.Combine(corpus, "forms");
        var forms = Directory.Exists(formsDirectory)
            ? Directory.GetFiles(formsDirectory, "*.tsv").OrderBy(f => f, StringComparer.Ordinal).SelectMany(Corpus.ReadForms).ToList()
            : [];
        Corpus.EnsureDisjoint(anchors, holdout);
        inventory.Validate(anchors, holdout, forms);

        var data = BasicTrainer.Train(UdCounts.Read(udDirectory), WordLexicon.Words, anchors, inventory, BasicHyperparameters.Default);
        var blob = BasicTablesWriter.Write(data);
        _ = BasicTables.Load(blob);
        var path = Path.IsPathRooted(outputPath) ? outputPath : Path.Combine(repository, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, blob, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"Trained {data.Summary()}.");
        output.WriteLine($"Wrote {path}: {blob.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(blob))}.");
    }
}
