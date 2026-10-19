using WordSense.Builder.Data;

namespace WordSense.Builder.Tests;

/// <summary>
///     Tests for the reproducibility of the form tables: the committed tables are the regeneration of the committed inputs and
///     the pinned sources, and the check reports every difference.
/// </summary>
/// <remarks>
///     The pinned Open English WordNet and AGID files are not committed. The check that regenerates the tables reads them from
///     <c>artifacts/inputs</c> (or the files named by <c>WORDSENSE_OEWN</c> and <c>WORDSENSE_AGID</c>). When they are missing the
///     test is skipped, unless <c>WORDSENSE_REQUIRE_FORM_INPUTS</c> is <c>true</c>, in which case it fails; CI downloads the pinned
///     files and runs the <c>verify-forms</c> command, which fails on a missing input.
/// </remarks>
public sealed class FormReproducibilityTests : IDisposable
{
    private readonly TestDirectory _dir = new();

    /// <inheritdoc />
    public void Dispose() => _dir.Dispose();

    private static (string Oewn, string Agid)? PinnedInputs()
    {
        var root = DataFixture.RepositoryRoot();
        var oewn = Environment.GetEnvironmentVariable("WORDSENSE_OEWN") is { Length: > 0 } o ? o : Path.Combine(root, "artifacts", "inputs", "english-wordnet-2025.xml.gz");
        var agid = Environment.GetEnvironmentVariable("WORDSENSE_AGID") is { Length: > 0 } a ? a : Path.Combine(root, "artifacts", "inputs", "agid", "agid-2016.01.19", "infl.txt");
        if (File.Exists(oewn) && File.Exists(agid))
        {
            return (oewn, agid);
        }

        var required = string.Equals(Environment.GetEnvironmentVariable("WORDSENSE_REQUIRE_FORM_INPUTS"), "true", StringComparison.OrdinalIgnoreCase);
        var message = $"The pinned inputs of the form tables were not found ({oewn}, {agid}); see data/PROVENANCE.md.";
        Assert.False(required, message);
        Assert.Skip(message);
        return null;
    }

    private string TinyInputs(out string agid)
    {
        var oewn = Path.Combine(_dir.Root, "oewn.xml");
        File.Copy(DataFixture.SlicePath, oewn);
        agid = Path.Combine(_dir.Root, "infl.txt");
        File.WriteAllText(agid, "bag: N: bags\n");
        return oewn;
    }

    /// <summary>
    ///     Regenerating the tables from the pinned inputs reproduces the committed files byte for byte.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Verify_CommittedTables_AreTheRegeneration()
    {
        var (oewn, agid) = PinnedInputs()!.Value;
        var root = DataFixture.RepositoryRoot();
        var differences = FormGenerator.Verify(Path.Combine(root, "data"), Path.Combine(root, "corpus"), oewn, agid);
        Assert.Empty(differences);
    }

    /// <summary>
    ///     A regeneration from other inputs than the committed tables came from is reported as differences.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Verify_OtherInputs_ReportsDifferences()
    {
        var oewn = TinyInputs(out var agid);
        var root = DataFixture.RepositoryRoot();
        var differences = FormGenerator.Verify(Path.Combine(root, "data"), Path.Combine(root, "corpus"), oewn, agid, null, null);
        Assert.NotEmpty(differences);
    }

    /// <summary>
    ///     An input that does not match its SHA-256 pin is refused before anything is generated.
    /// </summary>
    [Fact]
    public void WordSenseBuilder_FormGenerator_Run_UnpinnedInput_IsRefused()
    {
        var oewn = TinyInputs(out var agid);
        var root = DataFixture.RepositoryRoot();
        var ex = Assert.Throws<InvalidDataException>(() => FormGenerator.Run(Path.Combine(root, "data"), Path.Combine(root, "corpus"), oewn, agid, _dir.Root));
        Assert.Contains("SHA-256", ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     The command fails with a message that says where the pinned input belongs when it is missing.
    /// </summary>
    [Fact]
    public async Task WordSenseBuilder_Program_VerifyForms_MissingInput_Fails()
    {
        await using var output = new StringWriter();
        await using var error = new StringWriter();
        var code = await Program.RunAsync(["verify-forms", "--repository", DataFixture.RepositoryRoot(), "--oewn", Path.Combine(_dir.Root, "missing.xml.gz")], output, error, TestContext.Current.CancellationToken);
        Assert.Equal(1, code);
        Assert.Contains("does not exist", error.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     An -ing noun has no plural spelled like its singular unless it is one of the invariant names.
    /// </summary>
    /// <param name="spelling">The spelling.</param>
    /// <param name="plural">True when the spelling has a plural analysis of its own entry.</param>
    [Theory]
    [InlineData("bowling", false)]
    [InlineData("sibling", false)]
    [InlineData("feeling", false)]
    [InlineData("herring", true)]
    [InlineData("offspring", true)]
    public void WordSenseBuilder_FormGenerator_Data_IngNounPlural_IsNotTheSingular(string spelling, bool plural)
    {
        var data = Path.Combine(DataFixture.RepositoryRoot(), "data");
        var schema = DataSchema.Load(Path.Combine(data, "schema.yaml"));
        var own = DataTables.Read(data, schema.Table("form")).Where(r => r[0] == spelling && r[2] == $"oewn-{spelling}-n" && r[3].Contains("Number=Plur", StringComparison.Ordinal));
        Assert.Equal(plural, own.Any());
    }
}
