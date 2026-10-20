// Copyright (c) DEMA Consulting
// Licensed under the MIT License. See LICENSE file in the project root for full license information.

using DemaConsulting.WordSense;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reader of the form-chooser corpus and the label rule that ties a row to the analyses of its form.
/// </summary>
public sealed class ChooserCorpusTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose()
    {
        _dir.Dispose();
    }

    private string Write(string relative, string content) => _dir.Write(relative, content);

    /// <summary>
    ///     A well-formed row is parsed with its form, label, words and target word.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_ReadSentences_ParsesRow()
    {
        // Arrange: used is the past of use and the adjective used; the label of the first is its lemma
        var path = Write("a.tsv", "used\tuse\tShe {used} a spoon.\n");

        // Act
        var row = Assert.Single(ChooserCorpus.ReadDirectory(Path.GetDirectoryName(path)!));

        // Assert
        Assert.Equal("used", row.Form);
        Assert.Equal("use", row.Label);
        Assert.Equal("a.tsv", row.Source);
        Assert.Equal(["She", "used", "a", "spoon", "."], row.Words);
        Assert.Equal("used", row.Words[row.TargetWordIndex]);
    }

    /// <summary>
    ///     The label of one analysis of a lemma is written lemma/features and is valid for the form.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_ReadSentences_LemmaAndFeaturesLabel_IsAccepted()
    {
        // Arrange
        var path = Write("a.tsv", "read\tread/Tense=Pres\tThey {read} every day.\nread\tread/Tense=Past+VerbForm=Part\tShe {read} it twice.\n");

        // Act
        var (rows, errors) = ChooserCorpus.ReadSentencesLenient(path);

        // Assert
        Assert.Empty(errors);
        Assert.Equal(2, rows.Count);
    }

    /// <summary>
    ///     A row that is not three columns, has an upper-case form, a target that is not the form, an unknown form or a
    ///     label of no analysis is described and skipped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_ReadSentences_InvalidRows_AreDescribed()
    {
        // Arrange
        var path = Write(
            "a.tsv",
            "used\tuse\n"
            + "Used\tuse\tShe {used} it.\n"
            + "used\tuse\tShe {use} it.\n"
            + "zzyzx\tuse\tShe {zzyzx} it.\n"
            + "used\tleave\tShe {used} it.\n"
            + "used\tuse\tShe used it.\n"
            + "used\tuse\tShe {used} a spoon.\n");

        // Act
        var (rows, errors) = ChooserCorpus.ReadSentencesLenient(path);

        // Assert
        Assert.Single(rows);
        Assert.Equal(6, errors.Count);
        Assert.Contains("expected 3 tab-separated columns", errors[0], StringComparison.Ordinal);
        Assert.Contains("lower-case", errors[1], StringComparison.Ordinal);
        Assert.Contains("is not the form", errors[2], StringComparison.Ordinal);
        Assert.Contains("not in the word image", errors[3], StringComparison.Ordinal);
        Assert.Contains("stands for no analysis", errors[4], StringComparison.Ordinal);
        Assert.Contains("{target}", errors[5], StringComparison.Ordinal);
    }

    /// <summary>
    ///     Reading a directory stops at the first problem; a missing directory has no rows.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_ReadDirectory_MalformedRow_Throws_AndMissingDirectoryIsEmpty()
    {
        // Arrange
        Write("bad/a.tsv", "used\tleave\tShe {used} it.\n");

        // Act and Assert
        Assert.Throws<InvalidDataException>(() => ChooserCorpus.ReadDirectory(Path.Join(_dir.Root, "bad")));
        Assert.Empty(ChooserCorpus.ReadDirectory(Path.Join(_dir.Root, "missing")));
    }

    /// <summary>
    ///     A sentence that occurs in the anchors and the holdout, or twice, is an error (compared after normalization).
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_Validate_DuplicateSentence_IsAnError()
    {
        // Arrange
        var anchors = ChooserCorpus.ReadDirectory(Path.GetDirectoryName(Write("anchors/a.tsv", "used\tuse\tShe {used} a spoon.\n"))!);
        var holdout = ChooserCorpus.ReadDirectory(Path.GetDirectoryName(Write("holdout/a.tsv", "used\tuse\tshe {used} a spoon\nused\tused\tA {used} car.\n"))!);

        // Act
        var duplicate = ChooserCorpus.Validate(anchors, holdout);
        var fine = ChooserCorpus.Validate(anchors, holdout.Skip(1).ToList());

        // Assert
        Assert.False(duplicate.IsValid);
        Assert.Contains("Duplicate sentence", duplicate.Errors[0], StringComparison.Ordinal);
        Assert.True(fine.IsValid);
    }

    /// <summary>
    ///     The label of an analysis is the label of its lemma and features when there is one, else the label of its lemma.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_ChooserCorpus_LabelIndexOf_PrefersTheExactLabel()
    {
        // Arrange
        Assert.True(WordLexicon.TryLookup("read", out var analyses));
        var past = analyses.First(a => a.PartOfSpeech == PartOfSpeech.Verb && a.Features.StartsWith("Tense=Past", StringComparison.Ordinal));
        var noun = analyses.First(a => a.PartOfSpeech == PartOfSpeech.Noun);
        string[] labels = ["read", "read/Tense=Past+VerbForm=Part", "read/Tense=Pres"];

        // Act and Assert
        Assert.Equal(1, ChooserCorpus.LabelIndexOf(labels, past));
        Assert.Equal(0, ChooserCorpus.LabelIndexOf(labels, noun));
        Assert.Equal(-1, ChooserCorpus.LabelIndexOf(["other"], noun));
        Assert.Empty(ChooserCorpus.AnalysesOf("zzyzx"));
    }
}
