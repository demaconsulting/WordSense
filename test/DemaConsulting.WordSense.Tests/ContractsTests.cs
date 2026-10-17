using System.Reflection;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the public contract types of the core package.
/// </summary>
public class ContractsTests
{
    /// <summary>
    ///     A word sense compares by value.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordSense_SameValues_AreEqual()
    {
        var details = new WordDetails("record", PartOfSpeech.Noun, null, null);

        Assert.Equal(new WordSense("record", details, 0.5), new WordSense("record", details, 0.5));
        Assert.NotEqual(new WordSense("record", details, 0.5), new WordSense("record", details, 0.6));
        Assert.True(typeof(WordSense).IsValueType);
    }

    /// <summary>
    ///     A word sense may carry no details.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordSense_NoDetails_HasNullDetails()
    {
        var sense = new WordSense("zzqxjw", null, 0);

        Assert.Null(sense.Details);
        Assert.Equal(0.0, sense.Confidence);
    }

    /// <summary>
    ///     The model contract needs no disposal.
    /// </summary>
    [Fact]
    public void WordSenseContracts_IWordSenseModel_IsNotDisposable()
    {
        Assert.False(typeof(IDisposable).IsAssignableFrom(typeof(IWordSenseModel)));
        Assert.False(typeof(IAsyncDisposable).IsAssignableFrom(typeof(IWordSenseModel)));
    }

    /// <summary>
    ///     The mirror options are a sealed record.
    /// </summary>
    [Fact]
    public void WordSenseContracts_MirrorOptions_IsSealedRecord()
    {
        Assert.True(typeof(MirrorOptions).IsSealed);
        Assert.NotNull(typeof(MirrorOptions).GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance));
    }

    /// <summary>
    ///     The mirror options default every optional value to null.
    /// </summary>
    [Fact]
    public void WordSenseContracts_MirrorOptions_Defaults_AreNull()
    {
        var options = new MirrorOptions(new Uri("http://localhost:8000/"));

        Assert.Equal(new Uri("http://localhost:8000/"), options.BaseUri);
        Assert.Null(options.BearerToken);
        Assert.Null(options.Credentials);
        Assert.Null(options.StoreRoot);
        Assert.Null(options.Timeout);
    }

    /// <summary>
    ///     Converting mirror options to text never reveals a token or a credential.
    /// </summary>
    [Fact]
    public void WordSenseContracts_MirrorOptions_ToString_RedactsSecrets()
    {
        var options = new MirrorOptions(new Uri("https://models.example.com/"), BearerToken: "s3cret-token");
        var credentials = new MirrorOptions(new Uri("https://models.example.com/"), Credentials: new System.Net.NetworkCredential("user", "s3cret-password"));

        Assert.DoesNotContain("s3cret-token", options.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("s3cret-password", credentials.ToString(), StringComparison.Ordinal);
        Assert.Contains("models.example.com", options.ToString(), StringComparison.Ordinal);
    }

    /// <summary>
    ///     A word details value exposes the members it was built with.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordDetails_Construct_ExposesMembers()
    {
        var details = new WordDetails("record", PartOfSpeech.Verb, "verb", "rih-KORD");

        Assert.Equal("record", details.Lemma);
        Assert.Equal(PartOfSpeech.Verb, details.PartOfSpeech);
        Assert.Equal("verb", details.Meaning);
        Assert.Equal("rih-KORD", details.Respelling);
    }

    /// <summary>
    ///     The part-of-speech enumeration has exactly the eight traditional values.
    /// </summary>
    [Fact]
    public void WordSenseContracts_PartOfSpeech_Values_AreTheEightClasses()
    {
        Assert.Equal(
            ["Noun", "Pronoun", "Verb", "Adjective", "Adverb", "Preposition", "Conjunction", "Interjection"],
            Enum.GetNames<PartOfSpeech>());
    }

    /// <summary>
    ///     The core assembly references neither ONNX Runtime nor a weights or vector library.
    /// </summary>
    [Fact]
    public void WordSenseContracts_CoreAssembly_HasNoOnnxOrWeightsReferences()
    {
        var references = typeof(WordLexicon).Assembly.GetReferencedAssemblies().Select(a => a.Name ?? string.Empty).ToList();

        Assert.DoesNotContain(references, name => name.Contains("Onnx", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, name => name.StartsWith("DemaConsulting.WordSense.", StringComparison.Ordinal));
    }
}
