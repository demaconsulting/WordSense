using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="OpenCorpusAttestation"/>: the tagged tokens of the open corpus attest the spellings of their lemmas.
/// </summary>
public sealed class OpenCorpusAttestationTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    /// <summary>
    ///     A token attests its spelling in lower case for its lemma and the part of speech; adverbs file under a, and tokens of
    ///     other parts of speech and comment lines attest nothing.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpusAttestation_Read_Tokens_AttestSpellingsForTheirLemmas()
    {
        // Arrange
        _dir.Write("corpus/open/representative/data/train.tsv",
            "# comment\n# sent_id = s1\n"
            + "s1-1\tCleaned\tclean\tVERB\tV\t_\tunanimous\n"
            + "s1-2\tsooner\tsoon\tADV\tR\t_\tunchecked\n"
            + "s1-3\tbags\tbag\tNOUN\tN\t_\tunanimous\n"
            + "s1-4\tthe\tthe\tDET\tD\t_\tunanimous\n"
            + "s1-5\tshort\n");
        _dir.Write("corpus/open/targeted/data/train.tsv", "t1-1\tmice\tmouse\tNOUN\tN\t_\tunanimous\n");

        // Act
        var attested = OpenCorpusAttestation.Read(Path.Join(_dir.Root, "corpus"));

        // Assert
        Assert.Contains("clean/v", attested["cleaned"]);
        Assert.Contains("clean", attested["cleaned"]);
        Assert.Contains("soon/a", attested["sooner"]);
        Assert.Contains("bag/n", attested["bags"]);
        Assert.Contains("mouse/n", attested["mice"]);
        Assert.False(attested.ContainsKey("the"));
        Assert.False(attested.ContainsKey("short"));
    }

    /// <summary>
    ///     An absent open corpus attests nothing, and a merge adds the lemmas of the second source.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_OpenCorpusAttestation_Merge_AddsTheLemmasOfTheSecondSource()
    {
        // Arrange
        var target = new Dictionary<string, HashSet<string>> { ["bags"] = ["bag"] };
        var source = new Dictionary<string, HashSet<string>> { ["bags"] = ["bag/n"], ["mice"] = ["mouse"] };

        // Act
        OpenCorpusAttestation.Merge(target, source);

        // Assert
        Assert.Empty(OpenCorpusAttestation.Read(Path.Join(_dir.Root, "nowhere")));
        Assert.Equal(new[] { "bag", "bag/n" }, target["bags"].Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("mouse", target["mice"]);
    }
}
