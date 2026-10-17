namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests for <see cref="BasicWordSenseModel"/>.
/// </summary>
public class BasicWordSenseModelTests
{
    /// <summary>
    ///     Making the model ready needs no resources and completes immediately.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_CompletesWithoutFiles()
    {
        var model = new BasicWordSenseModel();

        var task = model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    /// <summary>
    ///     A mirror is ignored: nothing is requested from it.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_IgnoresMirror()
    {
        var model = new BasicWordSenseModel();
        var mirror = new MirrorOptions(new Uri("http://127.0.0.1:1/unreachable"));

        var task = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

        Assert.True(task.IsCompletedSuccessfully);
        await task;
    }

    /// <summary>
    ///     A token cancelled before the call gives a cancelled task.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_Cancelled_ReturnsCanceledTask()
    {
        var model = new BasicWordSenseModel();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var task = model.EnsureModelAsync(null, cancelled.Token);

        Assert.True(task.IsCanceled);
    }

    /// <summary>
    ///     The model creates a classifier that answers.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Create_ReturnsClassifier()
    {
        IWordSenseModel model = new BasicWordSenseModel();
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        var results = await model.Create().ClassifyAsync(["Hello", "world"], TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Length);
    }

    /// <summary>
    ///     A classifier can be created without a preparation call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Create_WithoutEnsure_Works()
    {
        var classifier = new BasicWordSenseModel().Create();

        var results = await classifier.ClassifyAsync(["They", "record", "the", "record"], TestContext.Current.CancellationToken);

        Assert.Equal(PartOfSpeech.Verb, results[1].Details?.PartOfSpeech);
    }

    /// <summary>
    ///     One classifier gives the same answers when used from many threads at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Create_Classifier_IsThreadSafe()
    {
        var classifier = new BasicWordSenseModel().Create();
        string[] words = ["They", "will", "record", "the", "record"];
        var expected = await classifier.ClassifyAsync(words, TestContext.Current.CancellationToken);

        var runs = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ =>
            Task.Run(() => classifier.ClassifyAsync(words, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        Assert.All(runs, run => Assert.Equal(expected, run));
    }

    /// <summary>
    ///     Disposing twice is harmless.
    /// </summary>
    [Fact]
    public void BasicWordSenseModel_Dispose_CalledTwice_IsHarmless()
    {
        var model = new BasicWordSenseModel();

        model.Dispose();

        Assert.Null(Record.Exception(model.Dispose));
    }

    /// <summary>
    ///     Disposing from many threads at once never throws.
    /// </summary>
    [Fact]
    public void BasicWordSenseModel_Dispose_Concurrent_IsHarmless()
    {
        var model = new BasicWordSenseModel();
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();

        Parallel.For(0, 16, new ParallelOptions { MaxDegreeOfParallelism = 4 }, _ =>
        {
            try
            {
                model.Dispose();
            }
            catch (Exception ex)
            {
                exceptions.Add(ex);
            }
        });

        Assert.Empty(exceptions);
        Assert.Throws<ObjectDisposedException>(model.Create);
    }

    /// <summary>
    ///     Creating a classifier after disposal is rejected.
    /// </summary>
    [Fact]
    public void BasicWordSenseModel_Create_AfterDispose_ThrowsObjectDisposed()
    {
        var model = new BasicWordSenseModel();
        model.Dispose();

        Assert.Throws<ObjectDisposedException>(model.Create);
    }

    /// <summary>
    ///     Preparing after disposal is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_EnsureModelAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = new BasicWordSenseModel();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A classifier created earlier rejects every call once its model is disposed, whatever the words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task BasicWordSenseModel_Classifier_AfterModelDispose_ThrowsObjectDisposed()
    {
        var model = new BasicWordSenseModel();
        var classifier = model.Create();
        var before = await classifier.ClassifyAsync(["They", "record"], TestContext.Current.CancellationToken);
        model.Dispose();

        Assert.Equal(2, before.Length);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync(["They", "record"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => classifier.ClassifyAsync([], TestContext.Current.CancellationToken));
    }
}
