namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the overlap check between the training sentences of a head and the held-out parts.
/// </summary>
public sealed class TrainingOverlapTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static LeakageSet Set(string name, LeakageRole role, params string[] sentences) =>
        new(name, role, [.. sentences.Select((s, i) => (name + i, s))]);

    /// <summary>
    ///     A training sentence that a held-out part has too, after normalization, is an overlap; others are not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_FindsTheSameSentence()
    {
        (string, string)[] train = [("t1", "The old dog slept by the fire all day."), ("t2", "Nothing here matches anything at all.")];
        LeakageSet[] heldOut = [Set("dev", LeakageRole.Evaluation, "the OLD dog slept by the fire -- all day", "Something else entirely.")];

        var overlaps = LeakageCheck.FindTrainingOverlaps(train, heldOut);

        var overlap = Assert.Single(overlaps);
        Assert.Equal(("dev", "t1", "dev0", "sentence"), overlap);
    }

    /// <summary>
    ///     A run of eight words in common counts for an external part only, not for a part of the repository.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_RunOfWordsCountsForExternalPartsOnly()
    {
        (string, string)[] train = [("t1", "she said that the old dog slept by the fire")];
        const string Longer = "yesterday she said that the old dog slept by the fire again";

        var external = LeakageCheck.FindTrainingOverlaps(train, [Set("UD", LeakageRole.External, Longer)]);
        var internalPart = LeakageCheck.FindTrainingOverlaps(train, [Set("dev", LeakageRole.Evaluation, Longer)]);
        var shortRun = LeakageCheck.FindTrainingOverlaps(train, [Set("UD", LeakageRole.External, "she said that the old dog slept")]);

        Assert.Equal("run of 8 words", Assert.Single(external).Kind);
        Assert.Empty(internalPart);
        Assert.Empty(shortRun);
    }

    /// <summary>
    ///     The treebank files that exist in a directory are read as external parts, and the read set from its sentence column.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FromGoldDirectoryAndReadSet_ReadTheFiles()
    {
        _dir.Write("gold/ud_train.tsv", "1\tHello\tN\n1\tthere\tB\n2\tBye\tI\n");
        _dir.Write("gold/ud_test.tsv", "1\tGo\tV\n");
        var readSet = _dir.Write("read.tsv", "id\tsplit\tlabel\ttokens\tidx\tfamily\tsentence\nr1\tdev\tPAST\ta b\t0\tf\tI read it.\n\nr2\ttest\tPRESENT\tc\t1\tf\tWe read.\n");

        var sets = LeakageCheck.FromGoldDirectory(Path.Join(_dir.Root, "gold"));
        var read = LeakageCheck.FromReadSet(readSet);

        Assert.Equal(["UD-EWT train", "UD-EWT test"], sets.Select(s => s.Name));
        Assert.All(sets, s => Assert.Equal(LeakageRole.External, s.Role));
        Assert.Equal(["Hello there", "Bye"], sets[0].Sentences.Select(s => s.Sentence));
        Assert.Equal("read set", read.Name);
        Assert.Equal(["I read it.", "We read."], read.Sentences.Select(s => s.Sentence));
    }

    /// <summary>
    ///     A read set with a short line is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FromReadSet_ShortLine_Throws()
    {
        var path = _dir.Write("bad.tsv", "id\tsplit\tlabel\ttokens\tidx\tfamily\tsentence\nr1\tdev\tPAST\n");

        Assert.Throws<InvalidDataException>(() => LeakageCheck.FromReadSet(path));
    }

    /// <summary>
    ///     The held-out parts of a corpus are its evaluation parts, the treebank files and the read set, never its training parts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_HeldOutSets_ListsEvaluationAndExternalPartsOnly()
    {
        const string Header = "# corpus = t\n\n";
        const string Sentence = "# sent_id = a\n# source_id = d\n# text = Go now.\n1\tGo\tgo\tVERB\tV\t_\tunanimous\n2\tnow\tnow\tADV\tB\t_\tunanimous\n3\t.\t.\tPUNCT\tO\t_\tunanimous\n\n";
        _dir.Write("corpus/open/representative/data/train.tsv", Header + Sentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", Header + Sentence.Replace("Go now", "Stop now", StringComparison.Ordinal));
        _dir.Write("gold/ud_dev.tsv", "1\tGo\tV\n");
        var readSet = _dir.Write("read.tsv", "id\tsplit\tlabel\ttokens\tidx\tfamily\tsentence\nr1\tdev\tPAST\ta\t0\tf\tI read it.\n");

        var sets = LeakageCheck.HeldOutSets(Path.Join(_dir.Root, "corpus"), Path.Join(_dir.Root, "data"), Path.Join(_dir.Root, "gold"), readSet);

        Assert.Equal(["open corpus dev", "UD-EWT dev", "read set"], sets.Select(s => s.Name));
    }
}
