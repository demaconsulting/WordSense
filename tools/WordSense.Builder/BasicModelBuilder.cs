using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.WordSense;
using DemaConsulting.WordSense.Basic;

#pragma warning disable S6966 // the tool writes small console and file outputs; asynchronous writes add nothing

namespace WordSense.Builder;

/// <summary>
///     The commands that create the table of the basic model: <c>open-counts</c> (the tag statistics of the open corpus)
///     and <c>train-basic</c> (the complete retraining, the one command to run after changing anything).
/// </summary>
internal static class BasicModelBuilder
{
    /// <summary>
    ///     The path of the table below the repository root.
    /// </summary>
    public static readonly string DefaultOutput = Path.Join("src", "DemaConsulting.WordSense.Basic", "Resources", "basic.bin");

    /// <summary>
    ///     Counts the tag statistics of the <c>train</c> split of the representative part of the open corpus (every token,
    ///     whatever its consensus status) and writes the count files.
    /// </summary>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="outputDirectory">The directory of the count files (<c>corpus/open/counts</c>).</param>
    /// <returns>A one-line summary.</returns>
    /// <exception cref="FileNotFoundException">Thrown when the training split does not exist.</exception>
    public static string WriteCounts(string corpus, string outputDirectory)
    {
        var sentences = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "train");
        var counts = TaggerCounts.Count(sentences.Select(OpenCorpus.ToTagged));
        counts.Write(outputDirectory);
        var tokens = counts.Unigrams.Take(PosTag.Count).Sum();
        return $"Counted {sentences.Count.ToString(CultureInfo.InvariantCulture)} sentences ({tokens.ToString(CultureInfo.InvariantCulture)} tokens) into {counts.Words.Count.ToString(CultureInfo.InvariantCulture)} words in {outputDirectory}.";
    }

    /// <summary>
    ///     Retrains the basic model completely and writes its table.
    /// </summary>
    /// <param name="repository">The repository root.</param>
    /// <param name="corpus">The corpus directory.</param>
    /// <param name="outputPath">The table path.</param>
    /// <param name="output">Receives messages.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task that completes when the table is written.</returns>
    /// <exception cref="InvalidDataException">Thrown when the corpus is inconsistent or the count files are missing.</exception>
    public static async Task TrainAsync(string repository, string corpus, string outputPath, TextWriter output, CancellationToken cancellationToken)
    {
        var countsDirectory = OpenCorpus.CountsDirectory(corpus);
        if (!File.Exists(Path.Join(countsDirectory, TaggerCounts.NgramsFile)) || !File.Exists(Path.Join(countsDirectory, TaggerCounts.WordsFile)))
        {
            throw new InvalidDataException($"The tag counts are missing in {countsDirectory}; run the 'open-counts' command first.");
        }

        var anchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var choosers = ChooserCorpus.ReadDirectory(Path.Join(corpus, "choosers", "anchors"));
        var data = BasicTrainer.Train(TaggerCounts.Read(countsDirectory), key => WordLexicon.TryLookup(key, out var found) ? found : [], anchors, choosers, BasicHyperparameters.Default);
        var blob = BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash);
        _ = BasicTables.Load(blob, WordLexicon.Current);
        var path = Path.IsPathRooted(outputPath) ? outputPath : Path.Join(repository, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, blob, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"Trained {data.Summary()}.");
        output.WriteLine($"Wrote {path}: {blob.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(blob))}.");
    }
}
