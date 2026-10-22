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
            "# comment\n\n# sent_id = s1\n# source_id = d1\n# text = Cleaned sooner bags the.\n"
            + "1\tCleaned\tclean\tVERB\tV\t_\tunanimous\n"
            + "2\tsooner\tsoon\tADV\tR\t_\tunchecked\n"
            + "3\tbags\tbag\tNOUN\tN\t_\tunanimous\n"
            + "4\tthe\tthe\tDET\tD\t_\tunanimous\n");
        _dir.Write("corpus/open/targeted/data/train.tsv", "# sent_id = t1\n# source_id = d2\n# text = mice\n1\tmice\tmouse\tNOUN\tN\t_\tunanimous\n");

        // Act
        var attested = OpenCorpusAttestation.Read(Path.Join(_dir.Root, "corpus"));

        // Assert
        Assert.Contains("clean/v", attested["cleaned"]);
        Assert.Contains("clean", attested["cleaned"]);
        Assert.Contains("soon/a", attested["sooner"]);
        Assert.Contains("bag/n", attested["bags"]);
        Assert.Contains("mouse/n", attested["mice"]);
        Assert.False(attested.ContainsKey("the"));
    }

    /// <summary>
    ///     A token line that is short, outside a sentence or without a lemma is refused with its file and line instead of being
    ///     skipped.
    /// </summary>
    /// <param name="content">The training split.</param>
    /// <param name="line">The line named by the message.</param>
    [Theory]
    [InlineData("# sent_id = s1\n1\tshort\n", 2)]
    [InlineData("1\tbags\tbag\tNOUN\tN\t_\tunanimous\n", 1)]
    [InlineData("# sent_id = s1\n1\tbags\t\tNOUN\tN\t_\tunanimous\n", 2)]
    [InlineData("# sent_id = s1\nx\tbags\tbag\tNOUN\tN\t_\tunanimous\n", 2)]
    public void WordSenseBuilder_OpenCorpusAttestation_Read_MalformedTokenLine_IsRefusedWithFileAndLine(string content, int line)
    {
        // Arrange
        _dir.Write("corpus/open/representative/data/train.tsv", content);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => OpenCorpusAttestation.Read(Path.Join(_dir.Root, "corpus")));

        // Assert
        Assert.Contains($"train.tsv:{line.ToString(System.Globalization.CultureInfo.InvariantCulture)}:", ex.Message, StringComparison.Ordinal);
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
        Assert.Equal(["bag", "bag/n"], target["bags"].Order(StringComparer.Ordinal).ToArray());
        Assert.Contains("mouse", target["mice"]);
    }
}
