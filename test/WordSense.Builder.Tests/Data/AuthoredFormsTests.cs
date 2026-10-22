using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for <see cref="AuthoredForms"/>: the reader of the forms written for the project, which serves as the irregular
///     source, the attestation of generated forms and the veto of the decisions that a form does not exist.
/// </summary>
public sealed class AuthoredFormsTests : IDisposable
{
    private const string Header = "lemma\tpos\trule\tbatch_id\twriter\tverifier\tverdict\tresult\tlicence\tevidence\n";

    private const string Licence = "CC0 1.0 (project-authored)";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    // The sidecar defaults to a header only; null leaves it out
    private string Lexicon(string forms, string? provenance = Header)
    {
        _dir.Write("lexicon/forms-authored.tsv", forms);
        if (provenance is not null)
        {
            _dir.Write("lexicon/forms-authored-provenance.tsv", provenance);
        }

        return Path.Join(_dir.Root, "lexicon");
    }

    private static string Row(string lemma = "table", string verdict = "agree", string result = "forms", string licence = Licence, string rule = "Plural", string evidence = "-") =>
        $"{lemma}\tN\t{rule}\ta1+v1\tw\tv\t{verdict}\t{result}\t{licence}\t{evidence}\n";

    /// <summary>
    ///     The rows fill the forms with their levels and the attestation map of every spelling.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_Rows_FillFormsAndAttestation()
    {
        // Arrange: a plural with a variant, an adverb that inflects and a verb past
        var lexicon = Lexicon("# comment\nabacus\tN\tPlural\tabacuses abaci\t0 1\nsoon\tB\tComparative\tsooner\t0\nhold\tV\tPast\theld\t0\n");

        // Act
        var authored = AuthoredForms.Read(lexicon);

        // Assert: the levels are kept and each spelling attests its lemma with the part of speech (adverbs under a)
        Assert.Equal(new[] { ("abacuses", "0"), ("abaci", "1") }, authored.Forms[("abacus", "n")].Plural);
        Assert.Equal(new[] { ("held", "0") }, authored.Forms[("hold", "v")].Past);
        Assert.Contains("abacus/n", authored.Attested["abaci"]);
        Assert.Contains("soon/a", authored.Attested["sooner"]);
        Assert.Contains("hold", authored.Attested["held"]);
    }

    /// <summary>
    ///     A lemma that is capitalized in the file is keyed by its lower-case spelling, and the forms attest in lower case.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_CapitalizedLemma_IsKeyedInLowerCase()
    {
        var authored = AuthoredForms.Read(Lexicon("Hollands\tN\tPlural\tHollandses\t0\n"));

        Assert.True(authored.Forms.ContainsKey(("hollands", "n")));
        Assert.Contains("hollands/n", authored.Attested["hollandses"]);
    }

    /// <summary>
    ///     Missing files are empty lists.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_MissingFiles_GiveEmptyLists()
    {
        var authored = AuthoredForms.Read(Path.Join(_dir.Root, "nowhere"));

        Assert.Empty(authored.Forms);
        Assert.Empty(authored.Attested);
        Assert.Empty(authored.NoForm);
    }

    /// <summary>
    ///     A level that is not 0 or 1, or a row without one level for each form, is refused with the lemma.
    /// </summary>
    /// <param name="row">The row.</param>
    [Theory]
    [InlineData("abacus\tN\tPlural\tabaci\t2\n")]
    [InlineData("abacus\tN\tPlural\tabacuses abaci\t0\n")]
    [InlineData("abacus\tN\tPlural\tabaci\n")]
    public void WordSenseBuilder_AuthoredForms_Read_BadLevels_AreRefused(string row)
    {
        var ex = Assert.Throws<InvalidDataException>(() => AuthoredForms.Read(Lexicon(row)));

        Assert.Contains("abacus", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A rule that the generator does not know is refused.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_UnknownRule_IsRefused()
    {
        var ex = Assert.Throws<InvalidDataException>(() => AuthoredForms.Read(Lexicon("abacus\tN\tDual\tabaca\t0\n")));

        Assert.Contains("Dual", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Authored forms without the provenance sidecar are refused, and a sidecar without forms is accepted.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_FormsWithoutSidecar_AreRefused()
    {
        // Arrange / Act
        var ex = Assert.Throws<InvalidDataException>(() => AuthoredForms.Read(Lexicon("table\tN\tPlural\ttables\t0\n", null)));

        // Assert
        Assert.Contains(AuthoredForms.ProvenanceFile, ex.Message, StringComparison.Ordinal);
        Assert.Empty(AuthoredForms.Read(Path.Join(_dir.Root, "nowhere")).Forms);
    }

    /// <summary>
    ///     A malformed sidecar is refused with its path and line: a wrong header, a short or empty field, a licence other than
    ///     CC0, an unknown rule, an invalid verdict and result, and a repeated decision.
    /// </summary>
    /// <param name="provenance">The sidecar.</param>
    /// <param name="line">The line named by the message.</param>
    [Theory]
    [InlineData("lemma\tpos\trule\n", 1)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\n", 2)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\t\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n", 2)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\tw\tv\tagree\tforms\tMIT\t-\n", 2)]
    [InlineData(Header + "table\tX\tPlural\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n", 2)]
    [InlineData(Header + "table\tN\tVariant\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n", 2)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\tw\tv\tmaybe\tforms\tCC0 1.0 (project-authored)\t-\n", 2)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\tw\tv\tregular-accepted\tnone\tCC0 1.0 (project-authored)\t-\n", 2)]
    [InlineData(Header + "table\tN\tPlural\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\nTable\tN\tPlural\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n", 3)]
    public void WordSenseBuilder_AuthoredForms_Read_MalformedSidecar_IsRefusedWithPathAndLine(string provenance, int line)
    {
        // Arrange
        var lexicon = Lexicon("table\tN\tPlural\ttables\t0\n", provenance);

        // Act
        var ex = Assert.Throws<InvalidDataException>(() => AuthoredForms.Read(lexicon));

        // Assert
        Assert.Contains($"{AuthoredForms.ProvenanceFile}:{line.ToString(System.Globalization.CultureInfo.InvariantCulture)}:", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A row for a lemma spelled like the header word is a decision, not a second header.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_LemmaNamedLemma_IsADecision()
    {
        // Arrange
        var lexicon = Lexicon("lemma\tN\tPlural\tlemmata\t0\n", Header + Row("lemma", "agree", "none"));

        // Act
        var authored = AuthoredForms.Read(lexicon);

        // Assert
        Assert.Contains(("lemma", "n", "Plural"), authored.NoForm);
    }

    /// <summary>
    ///     Only a decision on which both authors agreed that the form does not exist is a veto.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_Read_Decisions_OnlyAgreedNoneIsAVeto()
    {
        // Arrange: the provenance has a header, an agreed none, an adjudicated none and an agreed form
        var provenance = "# comment\nlemma\tpos\trule\tbatch_id\twriter\tverifier\tverdict\tresult\tlicence\tevidence\n"
            + "scurf\tN\tPlural\ta1+v1\tw\tv\tagree\tnone\tCC0 1.0 (project-authored)\t-\n"
            + "pus\tN\tPlural\ta1+v1+d1\tw\tv\tadjudicated\tnone\tCC0 1.0 (project-authored)\tmass noun\n"
            + "table\tN\tPlural\ta1+v1\tw\tv\tagree\tforms\tCC0 1.0 (project-authored)\t-\n";

        var authored = AuthoredForms.Read(Lexicon("table\tN\tPlural\ttables\t0\n", provenance));

        Assert.Equal(new[] { ("scurf", "n", "Plural") }, authored.NoForm.ToArray());
    }

    /// <summary>
    ///     The irregular keys are the lemmas with a form that the regular rules do not give.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_AuthoredForms_IrregularKeys_ListsOnlyFormsTheRulesDoNotGive()
    {
        // Arrange: a regular noun, a noun with a Latin plural, a regular adjective, a verb with an irregular past
        var authored = AuthoredForms.Read(Lexicon(
            "table\tN\tPlural\ttables\t0\nabacus\tN\tPlural\tabacuses abaci\t0 1\nbig\tJ\tComparative\tbigger\t0\nrun\tV\tPast\tran\t0\nwalk\tV\tPast\twalked\t0\n"));

        // Act
        var keys = authored.IrregularKeys();

        // Assert
        Assert.Equal(new[] { ("abacus", "n"), ("run", "v") }, keys.OrderBy(k => k.Lemma, StringComparer.Ordinal).ToArray());
    }
}
