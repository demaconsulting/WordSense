using System.Globalization;
using System.Security.Cryptography;
using DemaConsulting.WordSense;

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
    public static readonly string DefaultOutput = Path.Join("src", "DemaConsulting.WordSense", "Resources", "basic.bin");

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
        var counts = TaggerCounts.Count(sentences.Select(OpenCorpus.ToTagged), key => WordLexicon.TryLookup(key, out var found) ? found : []);
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
        if (!File.Exists(Path.Join(countsDirectory, TaggerCounts.NgramsFile)) || !File.Exists(Path.Join(countsDirectory, TaggerCounts.WordsFile)) || !File.Exists(Path.Join(countsDirectory, TaggerCounts.ClauseNgramsFile)))
        {
            throw new InvalidDataException($"The tag counts are missing in {countsDirectory}; run the 'open-counts' command first.");
        }

        // The cue tables learn from the base-stage anchors only: the anchors of the stages added later are balanced per sense
        // for the encoder weights and, with the quota of the closed-class senses (need#auxiliary), over-weight the rare
        // analyses of the high-usage lemmas (a cue table of need answers "we need rest" as an auxiliary); with them the
        // Basic table gains no part-of-speech accuracy on any measured set
        var allAnchors = Corpus.ReadDirectory(Path.Join(corpus, "anchors"));
        var stageOf = Corpus.StagesOf(allAnchors);
        var anchors = allAnchors.Where(a => stageOf(a.Lemma) == Corpus.BaseStage).ToList();

        // The chooser rows of the stages added later are left out for the same reason (the rows of whiting and aging are stage 1 rows)
        var choosers = ChooserCorpus.ReadDirectory(Path.Join(corpus, "choosers", "anchors")).Where(c => Corpus.IsBaseStageFile(c.Source)).ToList();
        var training = OpenCorpus.ReadSplit(corpus, OpenCorpus.Representative, "train").Select(OpenCorpus.ToTagged).ToList();
        Func<string, System.Collections.Immutable.ImmutableArray<WordDetails>> lexicon = key => WordLexicon.TryLookup(key, out var found) ? found : [];
        var trained = BasicTrainer.Train(
            TaggerCounts.Read(countsDirectory),
            lexicon,
            anchors,
            choosers,
            BasicHyperparameters.Default,
            training);

        // The reliability of each cue table from the running text of the training split (never the development or test splits)
        var reliable = await CueRunningReliability.ApplyAsync(trained, training, lexicon, cancellationToken).ConfigureAwait(false);

        // The noun/verb table corrects the odds that the model so far gives the words of the same split when they are held out
        var data = await NounVerbStacker.ApplyAsync(reliable, training, lexicon, cancellationToken).ConfigureAwait(false);
        var blob = BasicTablesWriter.Write(data, WordLexicon.Current.InventoryHash);
        _ = BasicTables.Load(blob, WordLexicon.Current);
        var path = Path.IsPathRooted(outputPath) ? outputPath : Path.Join(repository, outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, blob, cancellationToken).ConfigureAwait(false);
        output.WriteLine($"Trained {data.Summary()}.");
        output.WriteLine($"Wrote {path}: {blob.Length.ToString("N0", CultureInfo.InvariantCulture)} bytes, SHA-256 {Convert.ToHexString(SHA256.HashData(blob))}.");
    }
}
