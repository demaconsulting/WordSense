using System.Net;
using DemaConsulting.WordSense.MiniLM;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests for <see cref="OnnxWordSenseModel"/>. The file management tests use the encoder model of the MiniLM
///     package and its real model files; the tests that classify use a fixture model with the same model files but
///     with weights of its own over the real senses of the word image, so that they do not depend on the weights that
///     are embedded in the model packages. They are skipped when the model files are not available.
/// </summary>
public class OnnxWordSenseModelTests
{
    private static readonly string[] ModelFiles = ["model_int8.onnx", "vocab.txt"];

    private static readonly string[] PendingSentence = ["It", "was", "just", "fine"];

    private static MiniLMEncoderModel Open(string? directory = null) => new(directory, 128) { IntraOpThreads = 2 };

    private static FixtureEncoderModel Fixture(string? directory = null) => new(directory);

    private static string MissingDirectory() => Path.Join(Path.GetTempPath(), "wordsense-missing-" + Guid.NewGuid().ToString("N"));

    private static Task ServeModel(HttpListenerContext context, string directory) =>
        LocalServer.SendFileAsync(context, Path.Join(directory, Path.GetFileName(context.Request.Url!.AbsolutePath)));

    /// <summary>
    ///     The identity is available before any file is.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_ModelIdAndHiddenSize_AreAvailableBeforeEnsure()
    {
        using var model = Open();

        Assert.Equal("all-MiniLM-L6-v2-int8", model.ModelId);
        Assert.Equal(384, model.HiddenSize);
    }

    /// <summary>
    ///     A sequence length outside 8 to 512 is rejected.
    /// </summary>
    /// <param name="length">The sequence length.</param>
    [Theory]
    [InlineData(7)]
    [InlineData(513)]
    public void OnnxWordSenseModel_Constructor_SequenceLengthOutOfRange_Throws(int length)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new MiniLMEncoderModel(null, length));
    }

    /// <summary>
    ///     A caller-supplied directory is verified without network access.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_ModelDirectory_VerifiesEveryFile()
    {
        // Arrange: a directory with the real, verified model files
        using var model = Open(ModelAssets.RequireModelDirectory());

        // Act / Assert: every file verifies, so the ensure completes
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     A list of single-sense and unknown words is answered without any model file: no ensure, no download, no ONNX.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_LexiconOnlyWords_NeverTouchesModelFiles()
    {
        // Arrange: the directory does not exist, so an ensure would throw FileNotFoundException
        var missing = MissingDirectory();
        using var model = Fixture(missing);

        // Act
        var result = await model.ClassifyAsync(["The", "zzqxjw", "."], TestContext.Current.CancellationToken);

        // Assert: the known word has details, the unknown ones have none, and nothing touched the directory
        Assert.Equal(PartOfSpeech.Determiner, result[0].Details!.PartOfSpeech);
        Assert.NotEqual(SenseLevel.None, result[0].Level);
        Assert.Equal(ClassifyResult.Unknown("zzqxjw"), result[1]);
        Assert.Null(result[2].Details);
        Assert.Equal(SenseLevel.None, result[2].Level);
        Assert.False(Directory.Exists(missing));
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     An already canceled token is honored even when the list is empty or needs no model: the call faults with
    ///     <see cref="OperationCanceledException"/> before any work.
    /// </summary>
    /// <param name="word">The single word of the list, or null for an empty list.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData(null)]
    [InlineData("The")]
    public async Task OnnxWordSenseModel_ClassifyAsync_CanceledTokenWithoutModelWork_Throws(string? word)
    {
        var missing = MissingDirectory();
        using var model = Fixture(missing);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => model.ClassifyAsync(word is null ? [] : [word], cts.Token));
        Assert.False(Directory.Exists(missing));
    }

    /// <summary>
    ///     Ambiguous words whose lemma has no weights are answered from the lexicon at the part of speech level, every
    ///     analysis equally likely and no sense scored: no ensure, no model file, no ONNX.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_UntrainedAmbiguousWords_AnswerPartOfSpeechWithoutModelFiles()
    {
        // Arrange: the directory does not exist, so an ensure would throw FileNotFoundException; "record" has no weights
        var missing = MissingDirectory();
        using var model = Fixture(missing);

        // Act
        var result = await model.ClassifyAsync(["They", "will", "record", "the", "record", "."], TestContext.Current.CancellationToken);

        // Assert: "record" has both analyses, equally likely, at the part of speech level, with no group, sense or
        // scored senses, and no file was touched
        foreach (var index in new[] { 2, 4 })
        {
            Assert.Equal(SenseLevel.PartOfSpeech, result[index].Level);
            Assert.Equal("record", result[index].Details?.Lemma);
            Assert.Null(result[index].GroupId);
            Assert.Null(result[index].SenseId);
            Assert.Equal(2, result[index].Analyses.Length);
            Assert.All(result[index].Analyses, a => Assert.Equal(0.5, a.Probability));
            Assert.All(result[index].Analyses, a => Assert.True(a.Senses.IsEmpty));
        }

        Assert.False(Directory.Exists(missing));
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     A null list is rejected through the returned task.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_NullWords_Throws()
    {
        // Arrange
        using var model = Fixture();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => model.ClassifyAsync(null!, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A null element is rejected through the returned task.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_NullElement_Throws()
    {
        // Arrange
        using var model = Fixture();

        // Act / Assert
        await Assert.ThrowsAsync<ArgumentException>(() => model.ClassifyAsync(["record", null!], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A missing file in the supplied directory is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_MissingFile_Throws()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "vocab.txt"), Path.Join(directory, "vocab.txt"));
            using var model = Open(directory);

            await Assert.ThrowsAsync<FileNotFoundException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A file with the wrong content in the supplied directory is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AlteredFile_Throws()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "vocab.txt"), Path.Join(directory, "vocab.txt"));
            await File.WriteAllTextAsync(Path.Join(directory, "model_int8.onnx"), "altered", TestContext.Current.CancellationToken);
            using var model = Open(directory);

            await Assert.ThrowsAsync<InvalidDataException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     Concurrent preparation calls share one download.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_Concurrent_SharesOneDownload()
    {
        var source = ModelAssets.RequireModelDirectory();
        var store = Path.Join(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var server = new LocalServer(c => ServeModel(c, source));
            using var model = Open();
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken)));

            Assert.Equal(ModelFiles.Length, server.Requests.Count);
        }
        finally
        {
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }

    /// <summary>
    ///     A failed preparation can be retried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AfterFailure_CanBeRetried()
    {
        var source = ModelAssets.RequireModelDirectory();
        var store = Path.Join(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            var healthy = false;
            using var server = new LocalServer(c => healthy ? ServeModel(c, source) : LocalServer.SendAsync(c, 500, "broken"));
            using var model = Open();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            await Assert.ThrowsAsync<HttpRequestException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));
            healthy = true;
            await model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

            // The retry downloaded and verified every file
            Assert.NotEmpty(Directory.GetFiles(store, "*", SearchOption.AllDirectories));
        }
        finally
        {
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }

    /// <summary>
    ///     A model with an unavailable directory still classifies the words that need no model: it does not fail with
    ///     the ensure error, and the ensure itself still reports the missing directory.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MissingDirectory_DoesNotFail()
    {
        // Arrange: a directory that does not exist
        using var model = Fixture(MissingDirectory());

        // Act
        var result = await model.ClassifyAsync(["record"], TestContext.Current.CancellationToken);

        // Assert: the word is answered, while the explicit ensure still fails
        Assert.Single(result);
        await Assert.ThrowsAsync<FileNotFoundException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A model that was never ensured and is asked about an ambiguous word of a trained lemma with an unavailable
    ///     directory fails with the ensure error, not with an invalid-operation error.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MissingDirectory_ThrowsFileNotFound()
    {
        // Arrange
        using var model = Fixture(MissingDirectory());

        // Act / Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     An ambiguous word of a trained lemma makes the model ensure itself lazily, without a prior
    ///     <see cref="OnnxWordSenseModel.EnsureModelAsync"/> call, and open the encoder.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_WithoutEnsure_EnsuresLazily()
    {
        // Arrange
        using var model = Fixture(ModelAssets.RequireModelDirectory());
        Assert.True(model.IsEncoderReleased);

        // Act
        var result = await model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);

        // Assert: "just" was scored with the encoder, so each analysis lists the ranked senses of its part of speech
        Assert.False(model.IsEncoderReleased);
        Assert.Equal(2, result[2].Analyses.Length);
        Assert.All(result[2].Analyses, a => Assert.Equal(1.0, a.Senses.Sum(s => s.Probability), 6));
        Assert.Equal(1.0, result[2].Analyses.Sum(a => a.Probability), 6);
        Assert.Equal(SenseLevel.PartOfSpeech, result[2].Level);
    }

    /// <summary>
    ///     Concurrent classifications that need the model share one lazy ensure and give identical results.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_Concurrent_ShareOneLazyEnsure()
    {
        // Arrange
        using var model = Fixture(ModelAssets.RequireModelDirectory());

        // Act
        var runs = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ =>
            Task.Run(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken), TestContext.Current.CancellationToken)));

        // Assert
        Assert.All(runs, run => Assert.Equal(runs[0], run));
        Assert.False(model.IsEncoderReleased);
    }

    /// <summary>
    ///     A failed lazy ensure is retried by the next classification.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_AfterFailedLazyEnsure_CanBeRetried()
    {
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var model = Fixture(directory);

            await Assert.ThrowsAsync<FileNotFoundException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));
            Directory.CreateDirectory(directory);
            foreach (var name in ModelFiles)
            {
                File.Copy(Path.Join(source, name), Path.Join(directory, name));
            }

            var result = await model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);
            Assert.Equal(2, result[2].Analyses.Length);
            Assert.False(model.IsEncoderReleased);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    /// <summary>
    ///     Many classifications of one model share one encoder and one classifier.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_Repeated_ShareTheEncoder()
    {
        // Arrange
        using var model = Fixture(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        // Act: classify, register a classification, classify again, register another
        await model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);
        var first = model.AcquireClassifier();
        model.ReleaseClassifier();
        await model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);
        var second = model.AcquireClassifier();
        model.ReleaseClassifier();

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Many registrations of one ensured model share one classifier.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseModel_AcquireClassifier_Repeated_SharesTheClassifier()
    {
        // Arrange
        using var model = Fixture(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);

        // Act: register two classifications one after the other
        var first = model.AcquireClassifier();
        model.ReleaseClassifier();
        var second = model.AcquireClassifier();
        model.ReleaseClassifier();

        // Assert
        Assert.Same(first, second);
    }

    /// <summary>
    ///     Registering a classification before the model files are ensured is rejected.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_AcquireClassifier_NotEnsured_Throws()
    {
        // Arrange
        using var model = Fixture();

        // Act / Assert
        Assert.Throws<InvalidOperationException>(model.AcquireClassifier);
    }

    /// <summary>
    ///     Weights that do not belong to the word image fail the classification of a word that needs them, loudly.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_WeightsOfAnotherImage_Throws()
    {
        // Arrange: a lemma whose second sense does not exist in the image
        var ids = new[] { Fixtures.SenseIds("just")[0], "just#no-such-sense" };
        var lemma = new WeightedLemma("just", ids, Fixtures.Identity(2, FixtureEncoderModel.HiddenDimension, Fixtures.Scale, 1f, LemmaWeights.Never));
        using var model = new FixtureEncoderModel(MissingDirectory(), lemma);

        // Act / Assert: the weights are checked before any model file is needed
        await Assert.ThrowsAsync<InvalidDataException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A caller whose token is cancelled stops waiting, but the shared work is not cancelled.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_CancelledCaller_DoesNotCancelSharedWork()
    {
        using var model = Open(ModelAssets.RequireModelDirectory());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => model.EnsureModelAsync(null, cts.Token));

        // The shared ensure was not cancelled: a later call completes normally
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
    }

    /// <summary>
    ///     Disposing while a classification is registered keeps the model's resources until the classification
    ///     finishes; the last one to finish releases them, and new calls are rejected at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_DuringClassification_DefersEncoderRelease()
    {
        // Arrange: a registered (running) classification, which opens the encoder
        var model = Fixture(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        var classifier = model.AcquireClassifier();
        Assert.False(model.IsEncoderReleased);

        // Act
        model.Dispose();

        // Assert: the running classification still completes; new calls are refused
        Assert.False(model.IsEncoderReleased);
        var result = await classifier.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);
        Assert.Equal(2, result[2].Analyses.Length);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));
        Assert.Throws<ObjectDisposedException>(model.AcquireClassifier);

        // The last classification to finish releases the resources
        model.ReleaseClassifier();

        Assert.True(model.IsEncoderReleased);
        Assert.Null(Record.Exception(model.Dispose));
    }

    /// <summary>
    ///     Disposing while no classification runs releases the model's resources at once.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_Idle_ReleasesEncoderImmediately()
    {
        // Arrange: a model that has finished a classification that needed the encoder
        var model = Fixture(ModelAssets.RequireModelDirectory());
        await model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken);
        Assert.False(model.IsEncoderReleased);

        // Act
        model.Dispose();

        // Assert
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     Disposing twice is harmless.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_Dispose_CalledTwice_IsHarmless()
    {
        var model = Open();

        model.Dispose();

        var ex = Record.Exception(model.Dispose);
        Assert.Null(ex);
    }

    /// <summary>
    ///     Classifying after disposal is rejected, whatever the words.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = Fixture(ModelAssets.RequireModelDirectory());
        await model.EnsureModelAsync(null, TestContext.Current.CancellationToken);
        model.Dispose();

        // Any words, even a single-sense one, and an empty list are rejected: the disposal check comes first
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync([], TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Preparing after disposal is rejected.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AfterDispose_ThrowsObjectDisposed()
    {
        var model = Open();
        model.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Disposing from many threads at once releases once and never throws.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_Concurrent_IsHarmless()
    {
        var model = Fixture();

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
        await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync([], TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     Disposing while a download is in flight cancels it: the pending calls throw
    ///     <see cref="OperationCanceledException"/>, nothing hangs and no partial file remains.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_Dispose_DuringEnsure_CancelsDownload()
    {
        var store = Path.Join(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            // The server starts a response and then stalls until the test ends
            using var server = new LocalServer(async c =>
            {
                c.Response.StatusCode = 200;
                c.Response.ContentLength64 = 10_000_000;
                await c.Response.OutputStream.WriteAsync(new byte[16]);
                await c.Response.OutputStream.FlushAsync();
                await release.Task;
            });
            var model = Fixture();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new MirrorOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            var pending = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);
            var second = model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (server.Requests.Count == 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.NotEmpty(server.Requests);
            model.Dispose();

            // Every waiter ends with the cancellation exception, promptly
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));
            await Assert.ThrowsAsync<ObjectDisposedException>(() => model.ClassifyAsync(["record"], TestContext.Current.CancellationToken));

            release.TrySetResult();
            var files = Directory.Exists(store) ? Directory.GetFiles(store, "*", SearchOption.AllDirectories) : [];
            Assert.DoesNotContain(files, path => path.EndsWith(".part", StringComparison.Ordinal) || path.EndsWith(".onnx", StringComparison.Ordinal));
        }
        finally
        {
            release.TrySetResult();
            if (Directory.Exists(store))
            {
                Directory.Delete(store, true);
            }
        }
    }
}
