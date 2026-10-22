using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the use of the authored forms by <see cref="FormGenerator"/>: they are authoritative like the hand and closed
///     lists, a variant ships as a variant, an agreed decision that a form does not exist stops the regular rule, and the
///     reverse rules respect them.
/// </summary>
/// <remarks>
///     Each test generates the form table of the repository entries with a small corpus of its own, so only the file under test
///     differs.
/// </remarks>
public sealed class FormGeneratorAuthoredTests : IDisposable
{
    private const string ProvenanceHeader = "lemma\tpos\trule\tbatch_id\twriter\tverifier\tverdict\tresult\tlicence\tevidence\n";

    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private static string NoneRow(string lemma, string verdict) =>
        $"{lemma}\tN\tPlural\ta1+v1\tgpt-5.6-luna (LLM-written)\tgpt-6.1-sol (independent blind pass)\t{verdict}\tnone\tCC0 1.0 (project-authored)\t-\n";

    // Generates the form table with the given authored forms, provenance, dictionary and open corpus training split; returns the rows by spelling
    // A slice edit replaces a text of the Open English WordNet slice (to give an entry a written form)
    private ILookup<string, string[]> Generate(string name, string? authored, string? provenance = null, string lexicon = "", string? openTrain = null, (string From, string To)? sliceEdit = null)
    {
        var root = DataFixture.RepositoryRoot();
        var corpus = Path.Join(_dir.Root, name, "corpus");
        _dir.Write($"{name}/corpus/lexicon/form-frozen.tsv", "# no legacy analyses\n");
        _dir.Write($"{name}/corpus/lexicon/lexicon.tsv", lexicon);
        if (openTrain is not null)
        {
            _dir.Write($"{name}/corpus/open/representative/data/train.tsv", openTrain);
        }
        if (authored is not null)
        {
            _dir.Write($"{name}/corpus/lexicon/{AuthoredForms.FormsFile}", authored);
        }

        // The authored forms need their provenance file; a header only is an empty list of decisions
        if (authored is not null || provenance is not null)
        {
            _dir.Write($"{name}/corpus/lexicon/{AuthoredForms.ProvenanceFile}", provenance ?? ProvenanceHeader);
        }

        var oewn = Path.Join(_dir.Root, name, "oewn.xml");
        if (sliceEdit is { } edit)
        {
            var slice = File.ReadAllText(DataFixture.SlicePath);
            Assert.Contains(edit.From, slice, StringComparison.Ordinal);
            File.WriteAllText(oewn, slice.Replace(edit.From, edit.To, StringComparison.Ordinal));
        }
        else
        {
            File.Copy(DataFixture.SlicePath, oewn);
        }
        var output = Path.Join(_dir.Root, name, "out");
        Directory.CreateDirectory(output);
        FormGenerator.Run(Path.Join(root, "data"), corpus, oewn, output, null);
        return DataFixture.Rows(output, "form").ToLookup(r => r[0], StringComparer.Ordinal);
    }

    /// <summary>
    ///     A word with authored forms of a kind keeps the regular spelling only when the authors name it: the Latin plural
    ///     replaces the regular one, and it ships with the provenance llm.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_AuthoredPlural_ReplacesTheRegularSpelling()
    {
        // Arrange / Act: abacus has the authored plural abaci only; the dictionary lists abacuses, which would corroborate it
        var rows = Generate("replace", "abacus\tN\tPlural\tabaci\t0\n", null, "abacus\tN\nabacuses\tN\n");

        // Assert
        Assert.Contains(rows["abaci"], r => r[2] == "oewn-abacus-n" && r[3] == "Number=Plur" && r[^1] == "llm");
        Assert.DoesNotContain(rows["abacuses"], r => r[2] == "oewn-abacus-n" && r[3].Contains("Number=Plur", StringComparison.Ordinal));
    }

    /// <summary>
    ///     An authored variant (level 1) ships as a rule variant beside the primary form.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_AuthoredVariant_ShipsAsRuleVariant()
    {
        var rows = Generate("variant", "abacus\tN\tPlural\tabaci abacuses\t0 1\n");

        Assert.Contains(rows["abaci"], r => r[2] == "oewn-abacus-n" && r[^1] == "llm");
        Assert.Contains(rows["abacuses"], r => r[2] == "oewn-abacus-n" && r[3] == "Number=Plur" && r[^1] == "rule_variant");
    }

    /// <summary>
    ///     A decision on which both authors agreed that the plural does not exist stops the regular plural; an adjudicated
    ///     decision does not, and neither does a lemma without a decision.
    /// </summary>
    /// <param name="verdict">The verdict of the decision, or null for no decision.</param>
    /// <param name="shipped">True when the regular plural stays.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("adjudicated", true)]
    [InlineData("agree", false)]
    public void WordSenseBuilder_FormGenerator_Run_AgreedNoPlural_StopsTheRegularPlural(string? verdict, bool shipped)
    {
        // Arrange: scurf is a mass noun whose regular plural is a plain rule form
        var provenance = verdict is null ? null : ProvenanceHeader + NoneRow("scurf", verdict);

        // Act
        var rows = Generate("none-" + (verdict ?? "missing"), null, provenance);

        // Assert
        Assert.Equal(shipped, rows["scurfs"].Any(r => r[2] == "oewn-scurf-n" && r[3] == "Number=Plur"));
    }

    /// <summary>
    ///     The reverse rules do not trace a dictionary word to a lemma whose authored forms leave that spelling out: foots is no
    ///     plural of foot.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_AuthoredPlural_StopsTheReverseRuleOfTheDictionary()
    {
        // Arrange: the dictionary lists foots as a noun, and the authors gave feet only
        const string lexicon = "foot\tN\nfoots\tN\n";

        // Act
        var without = Generate("reverse-without", null, null, lexicon);
        var with = Generate("reverse-with", "foot\tN\tPlural\tfeet\t0\n", null, lexicon);

        // Assert: without the authored forms foots is a plural; with them it is not an analysis of foot
        Assert.Contains(without["foots"], r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.DoesNotContain(with["foots"], r => r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.Contains(with["feet"], r => r[2] == "oewn-foot-n");
    }

    /// <summary>
    ///     The authored forms join the written forms of the Open English WordNet for the same kind instead of being dropped: the
    ///     authored primary ships as a primary and the authored variant as a variant, and the regular spelling that the authors
    ///     leave out does not ship.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_AuthoredPlural_JoinsTheWrittenFormsOfTheLexicon()
    {
        // Arrange: the lexicon writes fishes; the authors name fish as the primary plural and pisces as a variant
        var edit = ("<Lemma writtenForm=\"fish\" partOfSpeech=\"n\"/>", "<Lemma writtenForm=\"fish\" partOfSpeech=\"n\"/><Form writtenForm=\"fishes\"/>");

        // Act
        var rows = Generate("join-oewn", "fish\tN\tPlural\tfish pisces\t0 1\n", null, "", null, edit);

        // Assert: the written form keeps its provenance, the authored forms ship beside it and the regular spelling is not generated
        Assert.Contains(rows["fishes"], r => r[2] == "oewn-fish-n" && r[3] == "Number=Plur" && r[^1] == "oewn_form");
        Assert.Contains(rows["fish"], r => r[2] == "oewn-fish-n" && r[3] == "Number=Plur" && r[^1] == "llm");
        Assert.Contains(rows["pisces"], r => r[2] == "oewn-fish-n" && r[3] == "Number=Plur" && r[^1] == "rule_variant");
        Assert.DoesNotContain(rows["fishs"], r => r[2] == "oewn-fish-n");
    }

    /// <summary>
    ///     The authored forms join the forms of the hand list for the same kind instead of being dropped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_AuthoredPlural_JoinsTheHandList()
    {
        // Arrange: the hand list gives fishes for fish; the authors add pisces
        _dir.Write($"join-hand/corpus/lexicon/{IrregularForms.HandFile}", "fish\tN\tPlural\tfishes\t0\n");

        // Act
        var rows = Generate("join-hand", "fish\tN\tPlural\tfishes pisces\t0 1\n");

        // Assert
        Assert.Contains(rows["fishes"], r => r[2] == "oewn-fish-n" && r[3] == "Number=Plur" && r[^1] == "hand");
        Assert.Contains(rows["pisces"], r => r[2] == "oewn-fish-n" && r[3] == "Number=Plur" && r[^1] == "rule_variant");
    }

    /// <summary>
    ///     A decision on which both authors agreed that the plural does not exist also stops the reverse rule of the dictionary:
    ///     the dictionary word superbias is no plural of superbia; an adjudicated decision does not stop it.
    /// </summary>
    /// <param name="verdict">The verdict of the decision, or null for no decision.</param>
    /// <param name="shipped">True when the plural spelling stays.</param>
    [Theory]
    [InlineData(null, true)]
    [InlineData("adjudicated", true)]
    [InlineData("agree", false)]
    public void WordSenseBuilder_FormGenerator_Run_AgreedNoPlural_StopsTheReverseRule(string? verdict, bool shipped)
    {
        // Arrange: the dictionary lists superbia and superbias, which the reverse rule would trace to the entry of superbia
        var provenance = verdict is null ? null : ProvenanceHeader + NoneRow("superbia", verdict);

        // Act
        var rows = Generate("reverse-none-" + (verdict ?? "missing"), null, provenance, "superbia\tN\nsuperbias\tN\n");

        // Assert
        Assert.Equal(shipped, rows["superbias"].Any(r => r[2] == "oewn-superbia-n" && r[3] == "Number=Plur"));
    }

    /// <summary>
    ///     A tagged token of the open corpus attests a comparative that the rule gives and nothing else corroborates: without
    ///     the token the degree form is not shipped.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_OpenCorpusToken_CorroboratesTheComparative()
    {
        // Arrange: a token line has the columns id, form, lemma, upos, collapsed tag, features and consensus
        const string train = "# comment\n\n# sent_id = s1\n# source_id = d1\n# text = Brighter.\n1\tbrighter\tbright\tADJ\tADJ\t_\tunanimous\n";

        // Act
        var without = Generate("open-without", null);
        var with = Generate("open-with", null, null, "", train);

        // Assert
        Assert.DoesNotContain(without["brighter"], r => r[2] == "oewn-bright-a" && r[3] == "Degree=Cmp");
        Assert.Contains(with["brighter"], r => r[2] == "oewn-bright-a" && r[3] == "Degree=Cmp");
    }
}
