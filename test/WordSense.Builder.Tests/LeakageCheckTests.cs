namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the leakage check: which shared sentences are leaks, and that the committed corpus has none.
/// </summary>
public sealed class LeakageCheckTests
{
    private static LeakageSet Set(string name, LeakageRole role, params string[] sentences) =>
        new(name, role, [.. sentences.Select((s, i) => (name + i, s))]);

    private static DirectoryInfo FindRepositoryRoot()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Join(root.FullName, "WordSense.slnx")))
        {
            root = root.Parent;
        }

        Assert.NotNull(root);
        return root;
    }

    /// <summary>
    ///     A sentence that a training part shares with an evaluation part is a leak, whatever its case, spacing and punctuation;
    ///     the other sentences are not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_Leakage_TrainingSentenceInAnEvaluationPart_IsALeak()
    {
        // Arrange
        var sets = new[]
        {
            Set("anchors", LeakageRole.Training, "The old dog slept by the fire all day.", "Another sentence about nothing at all here."),
            Set("holdout", LeakageRole.Evaluation, "the OLD dog slept by the fire -- all day", "A different sentence that nobody has seen before."),
        };

        // Act
        var report = LeakageCheck.Check(sets);

        // Assert
        var leak = Assert.Single(report.Leaks);
        Assert.Equal(("anchors", "holdout", 1), (leak.First, leak.Second, leak.Exact));
        Assert.Contains("LEAK", report.Render(), StringComparison.Ordinal);
        Assert.Contains("the old dog slept by the fire all day", report.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     Two training parts, a chooser part and its sense part, and an external set are not leaks; two evaluation parts and a
    ///     reference sentence of five words or more in the corpus are, and a reference stock phrase is not.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_Leakage_OnlyRealLeaksFail()
    {
        // Arrange
        const string Shared = "this sentence is shared between the parts today";
        var sets = new[]
        {
            Set("anchors", LeakageRole.Training, Shared, "i dare you"),
            Set("chooser anchors", LeakageRole.Training, Shared),
            Set("open corpus train", LeakageRole.Training, Shared),
            Set("holdout", LeakageRole.Evaluation, "a held out sentence with some words in it"),
            Set("chooser holdout", LeakageRole.Evaluation, "a held out sentence with some words in it"),
            Set("SemCor", LeakageRole.External, Shared, "a held out sentence with some words in it"),
            Set("OEWN examples", LeakageRole.Reference, "i dare you", "an example sentence of the dictionary written long ago"),
        };

        // Act
        var report = LeakageCheck.Check(sets);

        // Assert: nothing leaks (the stock phrase and the external set are the only other overlaps)
        Assert.Empty(report.Leaks);
        Assert.Contains(report.Overlaps, o => o is { First: "anchors", Second: "chooser anchors", Exact: 1 });

        // A second evaluation part with the sentence, and a long reference sentence in the corpus, are leaks
        var leaks = LeakageCheck.Check([.. sets, Set("suite", LeakageRole.Evaluation, "a held out sentence with some words in it"), Set("real text", LeakageRole.Evaluation, "An example sentence of the dictionary written long ago!")]).Leaks.ToList();
        Assert.Contains(leaks, o => o is { First: "holdout", Second: "suite" });
        Assert.Contains(leaks, o => o is { First: "OEWN examples", Second: "real text" });
        Assert.DoesNotContain(leaks, o => o.First == "SemCor" || o.Second == "SemCor");
    }

    /// <summary>
    ///     A run of eight words is reported as informational and never fails the check.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_Leakage_SharedRunOfWords_IsInformational()
    {
        // Arrange
        var sets = new[]
        {
            Set("anchors", LeakageRole.Training, "one two three four five six seven eight nine ten"),
            Set("holdout", LeakageRole.Evaluation, "zero one two three four five six seven eight eleven"),
        };

        // Act
        var report = LeakageCheck.Check(sets);

        // Assert
        Assert.Empty(report.Leaks);
        var overlap = Assert.Single(report.Overlaps);
        Assert.Equal((0, 1), (overlap.Exact, overlap.Gram));
        Assert.Contains("informational", report.Render(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     The committed corpus is disjoint: the anchors, held-out, chooser and real-text sentences, the suite, the
    ///     development and test splits of the open corpus, the challenge set and the example sentences of Open English WordNet
    ///     share no sentence (a training part and an evaluation part, two evaluation parts, or the corpus and the dictionary).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_Corpus_Disjointness_CoversSuiteEvaluationAndOewnExamples()
    {
        // Arrange
        var root = FindRepositoryRoot().FullName;

        // Act
        var sets = LeakageCheck.Load(Path.Join(root, "corpus"), Path.Join(root, "data"));
        var report = LeakageCheck.Check(sets);

        // Assert: every kind of part is checked, and none leaks
        var names = sets.Select(s => s.Name).ToList();
        foreach (var expected in new[] { "anchors", "chooser anchors", "holdout", "chooser holdout", "real text", "suite", "open corpus dev", "open corpus test", "STE challenge set", "OEWN examples" })
        {
            Assert.Contains(expected, names);
        }

        Assert.False(report.Leaks.Any(), report.Render());
    }

    /// <summary>
    ///     The command exits with 1 and lists the leak when a part of the corpus shares a sentence, and with 0 otherwise.
    /// </summary>
    /// <returns>A task that completes when the check is done.</returns>
    [Fact]
    public async Task WordSenseBuilder_Program_CheckLeakage_ExitsWithOneOnALeak()
    {
        // Arrange: a corpus with an anchor and a held-out sentence that are the same sentence
        using var dir = new TestDirectory();
        const string Sentence = "The old dog slept by the fire all day long.";
        dir.Write("corpus/anchors/a.tsv", "dog\tdog#x\tNoun\tThe old {dog} slept by the fire all day long.\n");
        dir.Write("corpus/holdout/h.tsv", $"dog\tdog#x\tNoun\t{Sentence}\n");
        using var output = new StringWriter();
        using var error = new StringWriter();

        // Act
        var code = await Program.RunAsync(["check-leakage", "--repository", dir.Root, "--cache", dir.Root], output, error, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(1, code);
        Assert.Contains("LEAK", output.ToString(), StringComparison.Ordinal);

        dir.Write("corpus/holdout/h.tsv", "dog\tdog#x\tNoun\tA completely different sentence about a cat.\n");
        using var clean = new StringWriter();
        Assert.Equal(0, await Program.RunAsync(["check-leakage", "--repository", dir.Root, "--cache", dir.Root], clean, error, TestContext.Current.CancellationToken));
    }
}
