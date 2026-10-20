using System.Collections.Immutable;
using System.Reflection;

namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the public contract types of the core package.
/// </summary>
public class ContractsTests
{
    private static readonly WordDetails Noun = new("record", PartOfSpeech.Noun, "Number=Sing");
    private static readonly WordDetails Verb = new("record", PartOfSpeech.Verb, "VerbForm=Inf");

    /// <summary>
    ///     A classification result compares by value, the arrays item by item.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_SameValues_AreEqual()
    {
        Assert.Equal(Result(0.5), Result(0.5));
        Assert.Equal(Result(0.5).GetHashCode(), Result(0.5).GetHashCode());
        Assert.NotEqual(Result(0.5), Result(0.6));
        Assert.NotEqual(
            new ClassifyResult("record", true, [new(Noun, 1, [])], Noun, SenseLevel.Sense, 1, "record#group:a", "record#b"),
            new ClassifyResult("record", true, [new(Noun, 1, [])], Noun, SenseLevel.Sense, 1, "record#group:a", "record#c"));
        Assert.True(typeof(ClassifyResult).IsValueType);
    }

    /// <summary>
    ///     A result of an unknown word has no details, no analyses and the level none.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_NoDetails_HasNullDetails()
    {
        var result = ClassifyResult.Unknown("zzqxjw");

        Assert.False(result.IsKnown);
        Assert.Null(result.Details);
        Assert.Empty(result.Analyses);
        Assert.Equal(0.0, result.Confidence);
        Assert.Equal(SenseLevel.None, result.Level);
        Assert.Equal("zzqxjw", result.Word);
    }

    /// <summary>
    ///     The level decides which identifiers a result carries: the sense level both ids (the group only when the sense is
    ///     in one), the group level the group id, the part-of-speech and none levels neither.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_LevelDecidesWhichIdsAreSet()
    {
        var details = new WordDetails("back", PartOfSpeech.Noun, "Number=Sing");
        var analyses = ImmutableArray.Create(new AnalysisCandidate(details, 1, []));
        var sense = new ClassifyResult("back", true, analyses, details, SenseLevel.Sense, 0.9, "back#group:football", "back#football-back");
        var ungrouped = new ClassifyResult("back", true, analyses, details, SenseLevel.Sense, 0.9, null, "back#body-part");
        var group = new ClassifyResult("back", true, analyses, details, SenseLevel.Group, 0.7, "back#group:football", null);
        var pos = new ClassifyResult("back", true, analyses, details, SenseLevel.PartOfSpeech, 0.8, null, null);

        Assert.True(WordLexicon.TryGetSense(sense.SenseId!, out var info));
        Assert.Equal(sense.GroupId, info.GroupId);
        Assert.Null(ungrouped.GroupId);
        Assert.NotNull(ungrouped.SenseId);
        Assert.Null(group.SenseId);
        Assert.True(WordLexicon.TryGetGroup(group.GroupId!, out var members));
        Assert.Contains("back#football-back", members.SenseIds);
        Assert.Null(pos.GroupId);
        Assert.Null(pos.SenseId);
    }

    /// <summary>
    ///     A default result is valid: its arrays are empty, its word is empty and reading it never throws.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_Default_HasEmptyArraysAndDoesNotThrow()
    {
        var result = default(ClassifyResult);
        var candidate = default(AnalysisCandidate);

        Assert.Empty(result.Analyses);
        Assert.False(result.Analyses.IsDefault);
        Assert.Equal(string.Empty, result.Word);
        Assert.Null(result.Details);
        Assert.Equal(result, default(ClassifyResult));
        Assert.Equal(result.GetHashCode(), default(ClassifyResult).GetHashCode());
        Assert.Empty(candidate.Senses);
        Assert.False(candidate.Senses.IsDefault);
        Assert.NotNull(candidate.Details);
        Assert.Empty(new ClassifyResult("x", true, default, null, SenseLevel.PartOfSpeech, 0, null, null).Analyses);
    }

    /// <summary>
    ///     The analyses and their senses are kept in the order they were given.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_Analyses_AreKeptInOrder()
    {
        var result = Result(0.75);

        Assert.Equal([Noun, Verb], result.Analyses.Select(a => a.Details));
        Assert.Equal([0.75, 0.25], result.Analyses.Select(a => a.Probability));
        Assert.Equal(["record#a", "record#b"], result.Analyses[0].Senses.Select(s => s.SenseId));
        Assert.Same(result.Details, result.Analyses[0].Details);
    }

    /// <summary>
    ///     Results built from separate but equal arrays are equal.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_SameAnalyses_AreEqual()
    {
        var first = Result(0.7);
        var second = Result(0.7);

        Assert.NotSame(
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(first.Analyses),
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(second.Analyses));
        Assert.Equal(first, second);
        Assert.True(first == second);
    }

    /// <summary>
    ///     Results whose analyses or senses differ are not equal.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_DifferentAnalyses_AreNotEqual()
    {
        var other = new ClassifyResult("record", true, [new(Noun, 0.7, [new("record#a", 0.6), new("record#b", 0.4)]), new(Verb, 0.3, [])], Noun, SenseLevel.PartOfSpeech, 0.7, null, null);

        Assert.NotEqual(Result(0.7), other);
        Assert.NotEqual(
            Result(0.7),
            new ClassifyResult("record", false, [new(Noun, 0.7, [new("record#a", 0.6), new("record#b", 0.4)]), new(Verb, 0.3, [])], Noun, SenseLevel.PartOfSpeech, 0.7, null, null));
    }

    /// <summary>
    ///     Unknown words share one empty array, so classifying text of unknown tokens allocates no array for them.
    /// </summary>
    [Fact]
    public void WordSenseContracts_ClassifyResult_Unknown_SharesEmptyArrays()
    {
        var first = ClassifyResult.Unknown("a1");
        var second = ClassifyResult.Unknown("b2");

        Assert.True(first.Analyses.IsEmpty);
        Assert.Same(
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(first.Analyses),
            System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(second.Analyses));
    }

    /// <summary>
    ///     The senses of an analysis are empty when they are not scored, also for a default array.
    /// </summary>
    [Fact]
    public void WordSenseContracts_AnalysisCandidate_Senses_DefaultIsEmpty()
    {
        var candidate = new AnalysisCandidate(Noun, 1, default);

        Assert.True(candidate.Senses.IsEmpty);
        Assert.False(candidate.Senses.IsDefault);
    }

    /// <summary>
    ///     Analysis candidates with the same values are equal, the senses compared item by item.
    /// </summary>
    [Fact]
    public void WordSenseContracts_AnalysisCandidate_SameValues_AreEqual()
    {
        var first = new AnalysisCandidate(Noun, 0.5, [new("record#a", 1)]);
        var second = new AnalysisCandidate(Noun, 0.5, [new("record#a", 1)]);

        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
        Assert.NotEqual(first, new AnalysisCandidate(Noun, 0.5, [new("record#b", 1)]));
        Assert.NotEqual(first, new AnalysisCandidate(Verb, 0.5, [new("record#a", 1)]));
    }

    /// <summary>
    ///     Sense candidates with the same values are equal.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseCandidate_SameValues_AreEqual()
    {
        Assert.Equal(new SenseCandidate("bow#archery", 0.48), new SenseCandidate("bow#archery", 0.48));
        Assert.NotEqual(new SenseCandidate("bow#archery", 0.48), new SenseCandidate("bow#violin", 0.48));
        Assert.NotEqual(new SenseCandidate("bow#archery", 0.48), new SenseCandidate("bow#archery", 0.41));
    }

    /// <summary>
    ///     The value of a feature is found by its name, also in the middle of several features.
    /// </summary>
    [Theory]
    [InlineData("Person=3+Tense=Pres", "Tense", "Pres")]
    [InlineData("Person=3+Tense=Pres", "Person", "3")]
    [InlineData("Number=Sing", "Number", "Sing")]
    [InlineData("Case=Nom+Number=Plur+Person=1", "Number", "Plur")]
    public void WordSenseContracts_WordDetails_TryGetFeature_FindsValue(string features, string name, string expected)
    {
        var details = new WordDetails("be", PartOfSpeech.Verb, features);

        Assert.True(details.TryGetFeature(name, out var value));
        Assert.Equal(expected, value);
    }

    /// <summary>
    ///     A missing feature, a prefix or suffix of a name, an empty name and empty features are not found.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordDetails_TryGetFeature_MissingFeature_ReturnsFalse()
    {
        var details = new WordDetails("be", PartOfSpeech.Verb, "Person=3+Tense=Pres");

        Assert.False(details.TryGetFeature("Number", out var value));
        Assert.Null(value);
        Assert.False(details.TryGetFeature("Tens", out _));
        Assert.False(details.TryGetFeature("Tense=Pres", out _));
        Assert.False(details.TryGetFeature(string.Empty, out _));
        Assert.False(new WordDetails("be", PartOfSpeech.Verb, string.Empty).TryGetFeature("Tense", out _));
        Assert.Throws<ArgumentNullException>(() => details.TryGetFeature(null!, out _));
    }

    private static ClassifyResult Result(double first) =>
        new(
            "record",
            true,
            [new(Noun, first, [new("record#a", 0.6), new("record#b", 0.4)]), new(Verb, 1 - first, [])],
            Noun,
            SenseLevel.PartOfSpeech,
            first,
            null,
            null);

    /// <summary>
    ///     The levels are the four answer levels.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseLevel_Values_AreTheFourLevels()
    {
        Assert.Equal(["None", "PartOfSpeech", "Group", "Sense"], Enum.GetNames<SenseLevel>());
        Assert.Equal(0, (int)SenseLevel.None);
    }

    /// <summary>
    ///     The levels increase with the precision of the answer.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseLevel_Order_IncreasesWithPrecision()
    {
        Assert.True(SenseLevel.None < SenseLevel.PartOfSpeech);
        Assert.True(SenseLevel.PartOfSpeech < SenseLevel.Group);
        Assert.True(SenseLevel.Group < SenseLevel.Sense);
    }

    /// <summary>
    ///     A sense record exposes the members it was built with.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseInfo_Construct_ExposesMembers()
    {
        var sense = new SenseInfo("bow#archery", "bow#group:weapon", "bow", PartOfSpeech.Noun, "bow%1:06:03::", "boh");

        Assert.Equal("bow#archery", sense.SenseId);
        Assert.Equal("bow#group:weapon", sense.GroupId);
        Assert.Equal("bow", sense.Lemma);
        Assert.Equal(PartOfSpeech.Noun, sense.PartOfSpeech);
        Assert.Equal("bow%1:06:03::", sense.SenseKey);
        Assert.Equal("boh", sense.Respelling);
    }

    /// <summary>
    ///     A project sense has no key, and a sense may be in no group.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseInfo_ProjectSense_HasNoKey()
    {
        var sense = new SenseInfo("the#determiner", null, "the", PartOfSpeech.Determiner, null, null);

        Assert.Null(sense.SenseKey);
        Assert.Null(sense.GroupId);
        Assert.Null(sense.Respelling);
    }

    /// <summary>
    ///     A group record exposes the members it was built with.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseGroup_Construct_ExposesMembers()
    {
        var group = new SenseGroup("back#group:football", "back", PartOfSpeech.Noun, ["back#football-back", "back#football-position"], null);

        Assert.Equal("back#group:football", group.GroupId);
        Assert.Equal("back", group.Lemma);
        Assert.Equal(PartOfSpeech.Noun, group.PartOfSpeech);
        Assert.Equal(2, group.SenseIds.Length);
        Assert.Null(group.Respelling);
    }

    /// <summary>
    ///     Sense records with the same values are equal.
    /// </summary>
    [Fact]
    public void WordSenseContracts_SenseInfo_SameValues_AreEqual()
    {
        Assert.Equal(
            new SenseInfo("bow#archery", null, "bow", PartOfSpeech.Noun, "bow%1:06:03::", null),
            new SenseInfo("bow#archery", null, "bow", PartOfSpeech.Noun, "bow%1:06:03::", null));
        Assert.NotEqual(
            new SenseInfo("bow#archery", null, "bow", PartOfSpeech.Noun, "bow%1:06:03::", null),
            new SenseInfo("bow#archery", null, "bow", PartOfSpeech.Noun, "bow%1:06:03::", "boh"));
    }

    /// <summary>
    ///     The model contract owns the lifetime, so it is disposable, and it is the only classification contract: the
    ///     removed classifier interface no longer exists.
    /// </summary>
    [Fact]
    public void WordSenseContracts_IWordSenseModel_IsDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(IWordSenseModel)));
        Assert.NotNull(typeof(IWordSenseModel).GetMethod("ClassifyAsync"));
        Assert.Null(typeof(IWordSenseModel).Assembly.GetType("DemaConsulting.WordSense.IWordSenseClassifier"));
        Assert.Null(typeof(IWordSenseModel).GetMethod("Create"));
    }

    /// <summary>
    ///     A test double implements the model contract without any model package: the contract is usable on its own.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSenseContracts_IWordSenseModel_TestDouble_IsUsableThroughTheInterface()
    {
        var model = new FakeModel();
        IWordSenseModel contract = model;

        await contract.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var result = await contract.ClassifyAsync(["a", "record"], TestContext.Current.CancellationToken);
        contract.Dispose();

        Assert.True(model.Ensured);
        Assert.True(model.Disposed);
        Assert.Equal(2, result.Length);
        Assert.Equal("record", result[1].Word);
    }

    /// <summary>
    ///     Word details compare by value.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordDetails_SameValues_AreEqual()
    {
        Assert.Equal(new WordDetails("record", PartOfSpeech.Noun, "Number=Sing"), new WordDetails("record", PartOfSpeech.Noun, "Number=Sing"));
        Assert.NotEqual(new WordDetails("record", PartOfSpeech.Noun, string.Empty), new WordDetails("record", PartOfSpeech.Verb, string.Empty));
        Assert.NotEqual(new WordDetails("record", PartOfSpeech.Noun, "Number=Sing"), new WordDetails("record", PartOfSpeech.Noun, string.Empty));
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
    ///     Converting mirror options to text hides user-info, query and fragment of the base address.
    /// </summary>
    [Fact]
    public void WordSenseContracts_MirrorOptions_ToString_HidesUserInfoQueryAndFragment()
    {
        var options = new MirrorOptions(new Uri("https://alice:hunter2@models.example.com:8443/wordsense?sig=abc123#frag-secret"));

        var text = options.ToString();

        Assert.Contains("https://models.example.com:8443/wordsense", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc123", text, StringComparison.Ordinal);
        Assert.DoesNotContain("frag-secret", text, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A bearer token and a credential are never printed by ToString, string interpolation or the record printer,
    ///     even with every other member set.
    /// </summary>
    [Fact]
    public void WordSenseContracts_MirrorOptions_ToString_NeverPrintsTokenOrCredentials()
    {
        var token = new MirrorOptions(new Uri("https://m.example/"), "tok-9f8e7d", null, "C:\\store", TimeSpan.FromSeconds(30));
        var basic = new MirrorOptions(
            new Uri("https://m.example/"),
            Credentials: new System.Net.NetworkCredential("svc-user", "pw-1a2b3c"),
            StoreRoot: "C:\\store");

        var tokenText = $"{token}";
        var basicText = $"{basic}";

        Assert.DoesNotContain("tok-9f8e7d", tokenText, StringComparison.Ordinal);
        Assert.Contains("BearerToken = ***", tokenText, StringComparison.Ordinal);
        Assert.Contains("Credentials = null", tokenText, StringComparison.Ordinal);
        Assert.DoesNotContain("svc-user", basicText, StringComparison.Ordinal);
        Assert.DoesNotContain("pw-1a2b3c", basicText, StringComparison.Ordinal);
        Assert.Contains("Credentials = ***", basicText, StringComparison.Ordinal);
        Assert.Contains("BearerToken = null", basicText, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A word details value exposes the members it was built with.
    /// </summary>
    [Fact]
    public void WordSenseContracts_WordDetails_Construct_ExposesMembers()
    {
        var details = new WordDetails("record", PartOfSpeech.Verb, "VerbForm=Inf");

        Assert.Equal("record", details.Lemma);
        Assert.Equal(PartOfSpeech.Verb, details.PartOfSpeech);
        Assert.Equal("VerbForm=Inf", details.Features);
        Assert.Null(typeof(WordDetails).GetProperty("SenseId"));
        Assert.Null(typeof(WordDetails).GetProperty("Meaning"));
        Assert.Null(typeof(WordDetails).GetProperty("Respelling"));
    }

    /// <summary>
    ///     The part-of-speech enumeration has the eight traditional values, Determiner and Auxiliary.
    /// </summary>
    [Fact]
    public void WordSenseContracts_PartOfSpeech_Values_AreTheTenClasses()
    {
        Assert.Equal(
            ["Noun", "Pronoun", "Verb", "Adjective", "Adverb", "Preposition", "Conjunction", "Interjection", "Determiner", "Auxiliary"],
            Enum.GetNames<PartOfSpeech>());
        Assert.Equal(9, (int)PartOfSpeech.Auxiliary);
    }

    private sealed class FakeModel : IWordSenseModel
    {
        public bool Ensured { get; private set; }

        public bool Disposed { get; private set; }

        public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default)
        {
            Ensured = true;
            return Task.CompletedTask;
        }

        public Task<ClassifyResult[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(words.Select(ClassifyResult.Unknown).ToArray());

        public void Dispose() => Disposed = true;
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
