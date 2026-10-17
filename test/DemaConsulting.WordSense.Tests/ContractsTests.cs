namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests for the public contract types of the core package.
/// </summary>
public class ContractsTests
{
    private sealed class FixedClassifier : IWordSenseClassifier
    {
        public Task<WordSense[]> ClassifyAsync(IEnumerable<string> words, CancellationToken cancellationToken = default) =>
            Task.FromResult(words.Select(w => new WordSense(w, null, 0)).ToArray());
    }

    private sealed class FixedModel : IWordSenseModel
    {
        public Task EnsureModelAsync(MirrorOptions? mirror = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public IWordSenseClassifier Create() => new FixedClassifier();
    }

    /// <summary>
    ///     A model implementation can be used through the interfaces alone.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task WordSense_Contracts_ModelAndClassifier_AreImplementable()
    {
        IWordSenseModel model = new FixedModel();
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var results = await model.Create().ClassifyAsync(["a", "b"], TestContext.Current.CancellationToken);

        Assert.Equal(["a", "b"], results.Select(r => r.Word));
        Assert.All(results, r => Assert.Null(r.Details));
    }

    /// <summary>
    ///     A word sense compares by value.
    /// </summary>
    [Fact]
    public void WordSense_Equality_IsByValue()
    {
        var details = new WordDetails("record", PartOfSpeech.Noun, null, null);

        Assert.Equal(new WordSense("record", details, 0.5), new WordSense("record", details, 0.5));
        Assert.NotEqual(new WordSense("record", details, 0.5), new WordSense("record", details, 0.6));
    }

    /// <summary>
    ///     The mirror options default every optional value to null.
    /// </summary>
    [Fact]
    public void MirrorOptions_Defaults_AreNull()
    {
        var options = new MirrorOptions(new Uri("http://localhost:8000/"));

        Assert.Equal(new Uri("http://localhost:8000/"), options.BaseUri);
        Assert.Null(options.BearerToken);
        Assert.Null(options.Credentials);
        Assert.Null(options.StoreRoot);
        Assert.Null(options.Timeout);
    }

    /// <summary>
    ///     The part-of-speech enumeration has the eight documented values.
    /// </summary>
    [Fact]
    public void PartOfSpeech_Values_AreEight()
    {
        Assert.Equal(8, Enum.GetValues<PartOfSpeech>().Length);
    }
}
