namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the overlap check between the training sentences of a head and the held-out parts.
/// </summary>
public sealed class TrainingOverlapTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    private const string ReadSetHeader = "id\tsplit\tlabel\ttokens\t" + "idx" + "\tfamily\tsentence\n";

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private static string FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root.FullName;
    }

    private static LeakageSet Set(string name, LeakageRole role, params string[] sentences) =>
        new(name, role, [.. sentences.Select((s, i) => (name + i, s))]);

    /// <summary>
    ///     A training sentence that a held-out part has too, after normalization, is an overlap; others are not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_FindsTheSameSentence()
    {
        // Arrange: a training set with one sentence that a held-out sentence repeats after normalization
        (string, string)[] train = [("t1", "The old dog slept by the fire all day."), ("t2", "Nothing here matches anything at all.")];
        LeakageSet[] heldOut = [Set("dev", LeakageRole.Evaluation, "the OLD dog slept by the fire -- all day", "Something else entirely.")];

        // Act: find the overlaps
        var overlaps = LeakageCheck.FindTrainingOverlaps(train, heldOut);

        // Assert: only the repeated sentence is reported
        var overlap = Assert.Single(overlaps);
        Assert.Equal(("dev", "t1", "dev0", "sentence"), overlap);
    }

    /// <summary>
    ///     A run of eight words in common counts for an external part only, not for a part of the repository.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_FindTrainingOverlaps_RunOfWordsCountsForExternalPartsOnly()
    {
        // Arrange: a training sentence and a longer sentence that contains it as an eight-word run
        (string, string)[] train = [("t1", "she said that the old dog slept by the fire")];
        const string Longer = "yesterday she said that the old dog slept by the fire again";

        // Act: check an external part, a repository part and an external part with only a seven-word run
        var external = LeakageCheck.FindTrainingOverlaps(train, [Set("UD", LeakageRole.External, Longer)]);
        var internalPart = LeakageCheck.FindTrainingOverlaps(train, [Set("dev", LeakageRole.Evaluation, Longer)]);
        var shortRun = LeakageCheck.FindTrainingOverlaps(train, [Set("UD", LeakageRole.External, "she said that the old dog slept")]);

        // Assert: only the long run in the external part is reported
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
        // Arrange: treebank train and test files in a gold directory, and a read set with a blank line
        _dir.Write("gold/ud_train.tsv", "1\tHello\tN\n1\tthere\tB\n2\tBye\tI\n");
        _dir.Write("gold/ud_test.tsv", "1\tGo\tV\n");
        var readSet = _dir.Write("read.tsv", ReadSetHeader + "r1\tdev\tPAST\ta b\t0\tf\tI read it.\n\nr2\ttest\tPRESENT\tc\t1\tf\tWe read.\n");

        // Act: read the gold directory and the read set
        var sets = LeakageCheck.FromGoldDirectory(Path.Join(_dir.Root, "gold"));
        var read = LeakageCheck.FromReadSet(readSet);

        // Assert: the treebank parts are external with joined sentences, and the read set uses its sentence column
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
        // Arrange: a read set whose only row has too few columns
        var path = _dir.Write("bad.tsv", ReadSetHeader + "r1\tdev\tPAST\n");

        // Act / Assert: reading the short line is refused
        Assert.Throws<InvalidDataException>(() => LeakageCheck.FromReadSet(path));
    }

    /// <summary>
    ///     The held-out parts of a corpus are its evaluation parts, the treebank files and the read set, never its training parts.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_HeldOutSets_ListsEvaluationAndExternalPartsOnly()
    {
        // Arrange: train and dev parts of an open corpus, a treebank dev file and a read set
        const string Header = "# corpus = t\n\n";
        const string Sentence = "# sent_id = a\n# source_id = d\n# text = Go now.\n1\tGo\tgo\tVERB\tV\t_\tunanimous\n2\tnow\tnow\tADV\tB\t_\tunanimous\n3\t.\t.\tPUNCT\tO\t_\tunanimous\n\n";
        _dir.Write("corpus/open/representative/data/train.tsv", Header + Sentence);
        _dir.Write("corpus/open/representative/data/dev.tsv", Header + Sentence.Replace("Go now", "Stop now", StringComparison.Ordinal));
        _dir.Write("gold/ud_dev.tsv", "1\tGo\tV\n");
        var readSet = _dir.Write("read.tsv", ReadSetHeader + "r1\tdev\tPAST\ta\t0\tf\tI read it.\n");

        // Act: list the held-out sets
        var sets = LeakageCheck.HeldOutSets(Path.Join(_dir.Root, "corpus"), Path.Join(_dir.Root, "data"), Path.Join(_dir.Root, "gold"), readSet);

        // Assert: the dev part, the treebank dev and the read set are listed, never the train part
        Assert.Equal(["open corpus dev", "UD-EWT dev", "read set"], sets.Select(s => s.Name));
    }

    /// <summary>
    ///     Without a gold directory and a read set the held-out parts are those of the repository only; a path that is
    ///     given and does not exist, or a gold directory without a gold file, stops the run instead of being skipped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_LeakageCheck_HeldOutSets_ExplicitPathThatDoesNotExist_Throws()
    {
        // Arrange: a corpus with a dev part, an empty gold directory, and paths that do not exist
        const string Sentence = "# corpus = t\n\n# sent_id = a\n# source_id = d\n# text = Go now.\n1\tGo\tgo\tVERB\tV\t_\tunanimous\n\n";
        _dir.Write("corpus/open/representative/data/dev.tsv", Sentence);
        Directory.CreateDirectory(Path.Join(_dir.Root, "empty-gold"));
        var corpus = Path.Join(_dir.Root, "corpus");
        var data = Path.Join(_dir.Root, "data");

        // Act: list the sets with no option, as the default behavior
        var none = LeakageCheck.HeldOutSets(corpus, data, null, null);

        // Assert: the default keeps the parts of the repository only, and each given path that cannot be used is an error naming it
        Assert.Equal(["open corpus dev"], none.Select(s => s.Name));
        var missingDirectory = Assert.Throws<DirectoryNotFoundException>(() => LeakageCheck.HeldOutSets(corpus, data, Path.Join(_dir.Root, "absent"), null));
        Assert.Contains("--gold-dir", missingDirectory.Message, StringComparison.Ordinal);
        var emptyDirectory = Assert.Throws<InvalidDataException>(() => LeakageCheck.HeldOutSets(corpus, data, Path.Join(_dir.Root, "empty-gold"), null));
        Assert.Contains("--gold-dir", emptyDirectory.Message, StringComparison.Ordinal);
        var missingReadSet = Assert.Throws<FileNotFoundException>(() => LeakageCheck.HeldOutSets(corpus, data, null, Path.Join(_dir.Root, "absent.tsv")));
        Assert.Contains("--read-set", missingReadSet.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The check-leakage command stops with exit code 1 and a message that names the option when a given gold
    ///     directory or read set does not exist, instead of checking fewer parts.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_CheckLeakage_ExplicitPathThatDoesNotExist_Fails()
    {
        // Arrange: the repository, and a gold directory and a read set that do not exist
        var root = FindRepositoryRoot();
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act: run the command with each option
        var gold = await Program.RunAsync(["check-leakage", "--repository", root, "--gold-dir", Path.Join(_dir.Root, "absent")], output, error, TestContext.Current.CancellationToken);
        var goldMessage = error.ToString();
        var read = await Program.RunAsync(["check-leakage", "--repository", root, "--read-set", Path.Join(_dir.Root, "absent.tsv")], output, error, TestContext.Current.CancellationToken);

        // Assert: both fail with a message that names the option
        Assert.Equal(1, gold);
        Assert.Equal(1, read);
        Assert.Contains("--gold-dir", goldMessage, StringComparison.Ordinal);
        Assert.Contains("--read-set", error.ToString(), StringComparison.Ordinal);
    }
}
