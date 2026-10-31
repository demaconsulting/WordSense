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

    private static Func<string, string?> MirrorEnvironment(string? user = null, string? password = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["WORDSENSE_MIRROR_URL"] = "https://models.example.com/wordsense",
            ["WORDSENSE_MIRROR_USER"] = user,
            ["WORDSENSE_MIRROR_PASSWORD"] = password,
        };
        return name => values.GetValueOrDefault(name);
    }

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
    ///     analysis equally likely and no sense scored, when the model description has no head: no ensure, no model file,
    ///     no ONNX.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_NoHead_UntrainedAmbiguousWords_AnswerPartOfSpeechWithoutModelFiles()
    {
        // Arrange: the directory does not exist, so an ensure would throw FileNotFoundException; "record" has no weights
        var missing = MissingDirectory();
        using var model = new FixtureEncoderModel(missing, spec => spec with { HeadResourceName = null });

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
    ///     With a head, a word of several parts of speech needs the encoder even when its lemma has no weights, so a
    ///     model with an unavailable directory fails with the ensure error; a text of words that need no encoder (single
    ///     part of speech, unknown) is answered without any file.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_WordOfSeveralPartsOfSpeech_NeedsTheEncoder()
    {
        // Arrange: "knowledge" has one part of speech; "record" has two
        var missing = MissingDirectory();
        using var model = Fixture(missing);
        Assert.Single(WordLexicon.Lookup("knowledge").Select(d => d.PartOfSpeech).Distinct());

        // Act
        var answered = await model.ClassifyAsync(["knowledge", "zzqxjw", "."], TestContext.Current.CancellationToken);
        var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.ClassifyAsync(["They", "will", "record", "."], TestContext.Current.CancellationToken));

        // Assert
        Assert.Equal(3, answered.Length);
        Assert.Equal("knowledge", answered[0].Details?.Lemma);
        Assert.NotNull(failure);
        Assert.False(Directory.Exists(missing));
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
    ///     A missing file in the supplied directory is reported as <see cref="ModelUnavailableException"/>, with the
    ///     original <see cref="FileNotFoundException"/> as the inner exception.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_MissingFile_ThrowsModelUnavailable()
    {
        // Arrange: a directory without the ONNX file
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "vocab.txt"), Path.Join(directory, "vocab.txt"));
            using var model = Open(directory);

            // Act
            var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

            // Assert: the original exception is kept
            Assert.IsType<FileNotFoundException>(failure.InnerException);
            Assert.Contains("model_int8.onnx", failure.Message, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A file with the wrong content in the supplied directory is reported as <see cref="ModelIntegrityException"/>,
    ///     with the original <see cref="InvalidDataException"/> as the inner exception.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AlteredFile_ThrowsModelIntegrity()
    {
        // Arrange: a directory whose ONNX file has the wrong content
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "vocab.txt"), Path.Join(directory, "vocab.txt"));
            await File.WriteAllTextAsync(Path.Join(directory, "model_int8.onnx"), "altered", TestContext.Current.CancellationToken);
            using var model = Open(directory);

            // Act
            var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

            // Assert: the original exception is kept, and the message names the file
            Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.Contains("model_int8.onnx", failure.Message, StringComparison.Ordinal);
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
            var mirror = new DownloadOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

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
    ///     A preparation that no source can serve is reported as <see cref="ModelUnavailableException"/> with the
    ///     <see cref="HttpRequestException"/> as the inner exception, and a failed preparation can be retried.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_AllSourcesFail_ThrowsModelUnavailable()
    {
        var source = ModelAssets.RequireModelDirectory();
        var store = Path.Join(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Arrange: the mirror and the public source both answer with an error until the server is healthy
            var healthy = false;
            using var server = new LocalServer(c => healthy ? ServeModel(c, source) : LocalServer.SendAsync(c, 500, "broken"));
            using var model = Open();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new DownloadOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            // Act
            var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));
            healthy = true;
            await model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken);

            // Assert: the original exception is kept, and the retry downloaded and verified every file
            Assert.IsType<HttpRequestException>(failure.InnerException);
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
    ///     When a source serves bytes with a wrong SHA-256, the file is discarded like any other failed source, and when
    ///     no source delivers a verified file the failure is <see cref="ModelUnavailableException"/> (not an integrity
    ///     failure): a hash mismatch of a download is a download failure.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_DownloadedHashMismatch_ThrowsModelUnavailable()
    {
        var store = Path.Join(Path.GetTempPath(), "wordsense-store-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Arrange: every source serves wrong bytes
            using var server = new LocalServer(c => LocalServer.SendAsync(c, 200, "wrong bytes"));
            using var model = Open();
            model.GitHubBaseUri = new Uri(server.BaseUri, "/lfs/");
            model.GetEnvironmentVariable = _ => null;
            var mirror = new DownloadOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

            // Act
            var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(mirror, TestContext.Current.CancellationToken));

            // Assert: no wrong file was kept
            Assert.IsType<HttpRequestException>(failure.InnerException);
            Assert.Empty(Directory.Exists(store) ? Directory.GetFiles(store, "*", SearchOption.AllDirectories) : []);
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
    ///     Invalid download options are reported as <see cref="DownloadConfigurationException"/> that is not an
    ///     <see cref="ArgumentException"/> and keeps the original <see cref="ArgumentException"/> (or its subclass
    ///     <see cref="ArgumentOutOfRangeException"/> for a timeout) as the inner exception, and a failed call can be
    ///     retried with valid options.
    /// </summary>
    /// <param name="kind">The invalid setting.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("relative")]
    [InlineData("scheme")]
    [InlineData("query")]
    [InlineData("tokenAndCredentials")]
    [InlineData("credentialsOverHttp")]
    [InlineData("zeroTimeout")]
    [InlineData("negativeTimeout")]
    [InlineData("emptyStoreRoot")]
    [InlineData("whitespaceStoreRoot")]
    [InlineData("invalidCharacterStoreRoot")]
    public async Task OnnxWordSenseModel_EnsureModelAsync_InvalidOptions_ThrowsDownloadConfiguration(string kind)
    {
        // Arrange: a model that downloads, and options that are invalid in one way
        using var model = Open();
        model.GetEnvironmentVariable = _ => null;
        var https = new Uri("https://models.example.com/wordsense");
        var options = kind switch
        {
            "relative" => new DownloadOptions(new Uri("relative/path", UriKind.Relative)),
            "scheme" => new DownloadOptions(new Uri("ftp://models.example.com/wordsense")),
            "query" => new DownloadOptions(new Uri("https://models.example.com/wordsense?sig=abc")),
            "tokenAndCredentials" => new DownloadOptions(https, BearerToken: "token", Credentials: new NetworkCredential("user", "password")),
            "credentialsOverHttp" => new DownloadOptions(new Uri("http://models.example.com/wordsense"), BearerToken: "token"),
            "zeroTimeout" => new DownloadOptions(https, Timeout: TimeSpan.Zero),
            "negativeTimeout" => new DownloadOptions(https, Timeout: TimeSpan.FromSeconds(-1)),
            "emptyStoreRoot" => new DownloadOptions(https, StoreRoot: string.Empty),
            "whitespaceStoreRoot" => new DownloadOptions(https, StoreRoot: "  \t "),
            "invalidCharacterStoreRoot" => new DownloadOptions(https, StoreRoot: "store\0root"),
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown kind."),
        };

        // Act
        var failure = await Assert.ThrowsAsync<DownloadConfigurationException>(() => model.EnsureModelAsync(options, TestContext.Current.CancellationToken));

        // Assert: a typed exception that is not an ArgumentException, with the original one as the inner exception
        Assert.False((object)failure is ArgumentException);
        Assert.IsType<WordSenseException>(failure, exactMatch: false);
        Assert.IsType<ArgumentException>(failure.InnerException, exactMatch: false);
        if (kind.EndsWith("Timeout", StringComparison.Ordinal))
        {
            Assert.IsType<ArgumentOutOfRangeException>(failure.InnerException);
        }

        if (kind.EndsWith("StoreRoot", StringComparison.Ordinal))
        {
            Assert.Equal("StoreRoot", ((ArgumentException)failure.InnerException).ParamName);
        }
    }

    /// <summary>
    ///     Invalid <c>WORDSENSE_MIRROR_*</c> settings are reported by the explicit ensure as
    ///     <see cref="DownloadConfigurationException"/>.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_InvalidEnvironment_ThrowsDownloadConfiguration()
    {
        // Arrange: a user without a password
        using var model = Open();
        model.GetEnvironmentVariable = MirrorEnvironment(user: "user");

        // Act
        var failure = await Assert.ThrowsAsync<DownloadConfigurationException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentException>(failure.InnerException, exactMatch: false);
    }

    /// <summary>
    ///     When a model directory is supplied, the download options are not read at all, so invalid options are ignored
    ///     and nothing is thrown.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_EnsureModelAsync_ModelDirectory_IgnoresInvalidOptions()
    {
        // Arrange: the real files in a directory, and options that are invalid in several ways
        using var model = Open(ModelAssets.RequireModelDirectory());
        var invalid = new DownloadOptions(new Uri("relative/path", UriKind.Relative), BearerToken: "token", Timeout: TimeSpan.Zero);

        // Act / Assert: no exception
        await model.EnsureModelAsync(invalid, TestContext.Current.CancellationToken);
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
        var result = await model.ClassifyAsync(["The"], TestContext.Current.CancellationToken);

        // Assert: the word is answered, while the explicit ensure still fails
        Assert.Single(result);
        await Assert.ThrowsAsync<ModelUnavailableException>(() => model.EnsureModelAsync(null, TestContext.Current.CancellationToken));
    }

    /// <summary>
    ///     A model that was never ensured and is asked about an ambiguous word of a trained lemma with an unavailable
    ///     directory fails with the ensure error, not with an invalid-operation error.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_MissingDirectory_ThrowsModelUnavailable()
    {
        // Arrange
        using var model = Fixture(MissingDirectory());

        // Act
        var failure = await Assert.ThrowsAsync<ModelUnavailableException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));

        // Assert: the lazy ensure maps like the explicit one, with the original exception kept
        Assert.IsType<IOException>(failure.InnerException, exactMatch: false);
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     Invalid <c>WORDSENSE_MIRROR_*</c> settings found by the lazy ensure of an ambiguous word are reported as
    ///     <see cref="DownloadConfigurationException"/>, and the call can be retried when the settings are fixed.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_InvalidMirrorEnvironment_ThrowsDownloadConfiguration()
    {
        // Arrange: a downloading model whose environment has a password without a user
        using var model = Fixture();
        model.GetEnvironmentVariable = MirrorEnvironment(password: "password");

        // Act
        var failure = await Assert.ThrowsAsync<DownloadConfigurationException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<ArgumentException>(failure.InnerException, exactMatch: false);
    }

    /// <summary>
    ///     An ONNX file that passes its pinned digest but cannot be opened as a model is reported as
    ///     <see cref="ModelIntegrityException"/>, with the original exception as the inner exception.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_UnreadableOnnxFile_ThrowsModelIntegrity()
    {
        // Arrange: a directory with the real vocabulary and an ONNX file that is not a model but is pinned as it is
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "vocab.txt"), Path.Join(directory, "vocab.txt"));
            var garbage = "this is not an onnx model"u8.ToArray();
            await File.WriteAllBytesAsync(Path.Join(directory, "model_int8.onnx"), garbage, TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(garbage)).ToLowerInvariant();
            using var model = new FixtureEncoderModel(directory, spec => spec with
            {
                Files = [.. spec.Files.Select(f => f.Name == spec.OnnxFileName ? f with { Sha256 = digest, Length = garbage.Length } : f)],
            });

            // Act
            var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));

            // Assert: the original exception of the ONNX runtime is kept, and the failed open leaves nothing behind
            Assert.NotNull(failure.InnerException);
            Assert.True(model.IsEncoderReleased);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     A vocabulary that passes its pinned digest but lacks a required special token is reported as
    ///     <see cref="ModelIntegrityException"/> with the <see cref="ArgumentException"/> of the tokenizer as the inner
    ///     exception, not as a raw argument exception.
    /// </summary>
    /// <param name="token">The required token that is removed from the vocabulary.</param>
    /// <returns>A task that completes when the check has run.</returns>
    [Theory]
    [InlineData("[UNK]")]
    [InlineData("[CLS]")]
    [InlineData("[SEP]")]
    public async Task OnnxWordSenseModel_ClassifyAsync_VocabularyWithoutRequiredToken_ThrowsModelIntegrity(string token)
    {
        // Arrange: the real ONNX file and a vocabulary without the token, pinned as it is
        var source = ModelAssets.RequireModelDirectory();
        var directory = Path.Join(Path.GetTempPath(), "wordsense-model-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            File.Copy(Path.Join(source, "model_int8.onnx"), Path.Join(directory, "model_int8.onnx"));
            var lines = (await File.ReadAllLinesAsync(Path.Join(source, "vocab.txt"), TestContext.Current.CancellationToken))
                .Where(line => line.Trim() != token)
                .ToArray();
            var vocabulary = System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines) + "\n");
            await File.WriteAllBytesAsync(Path.Join(directory, "vocab.txt"), vocabulary, TestContext.Current.CancellationToken);
            var digest = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(vocabulary)).ToLowerInvariant();
            using var model = new FixtureEncoderModel(directory, spec => spec with
            {
                Files = [.. spec.Files.Select(f => f.Name == spec.VocabularyFileName ? f with { Sha256 = digest, Length = vocabulary.Length } : f)],
            });

            // Act
            var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));

            // Assert: the original argument exception is kept, and the failed open leaves nothing behind
            Assert.IsType<ArgumentException>(failure.InnerException);
            Assert.Contains(token, failure.InnerException.Message, StringComparison.Ordinal);
            Assert.True(model.IsEncoderReleased);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    /// <summary>
    ///     An ONNX model whose hidden size differs from the one of the model description (and of its embedded weights)
    ///     is reported as <see cref="ModelIntegrityException"/> instead of being answered silently from the lexicon;
    ///     the rejected encoder is released.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_EncoderHiddenSizeMismatch_ThrowsModelIntegrity()
    {
        // Arrange: the real MiniLM files (hidden size 384) under a description and weights of hidden size 768
        const int wrongSize = 768;
        using var model = new FixtureEncoderModel(ModelAssets.RequireModelDirectory(), spec => spec with
        {
            HiddenSize = wrongSize,
            HeadResourceName = null,
            WeightsAssembly = new FixtureAssembly(
                FixtureEncoderModel.ResourceName,
                BlobWriter.Write(new WeightsData(spec.ModelId, Fixtures.InventoryHash(), wrongSize, new float[wrongSize], [Fixtures.Lemma("just", dimension: wrongSize)]))),
        });

        // Act
        var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));

        // Assert: both sizes are named, the original exception is kept, and the rejected encoder is not kept
        Assert.IsType<InvalidDataException>(failure.InnerException);
        Assert.Contains("384", failure.Message, StringComparison.Ordinal);
        Assert.Contains("768", failure.Message, StringComparison.Ordinal);
        Assert.Contains(model.ModelId, failure.Message, StringComparison.Ordinal);
        Assert.True(model.IsEncoderReleased);
    }

    /// <summary>
    ///     The embedded part-of-speech head is loaded and checked with the weights and needs no model file.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_EmbeddedHead_LoadsWithoutModelFiles()
    {
        // Arrange: a model whose directory does not exist, so that nothing can be read from it
        using var model = new FixtureEncoderModel(MissingDirectory());

        // Act: a text that needs no encoder still loads and checks the head
        var results = await model.ClassifyAsync(["the"], TestContext.Current.CancellationToken);

        // Assert
        Assert.Single(results);
        var head = model.GetHead();
        Assert.NotNull(head);
        Assert.Equal(FixtureEncoderModel.HiddenDimension, head.HiddenSize);
        Assert.Same(head, model.GetHead());
    }

    /// <summary>
    ///     A model description without a head resource has no head and loads.
    /// </summary>
    [Fact]
    public async Task OnnxWordSenseModel_GetHead_NoHeadResource_ReturnsNull()
    {
        // Arrange: a model description without a head resource, in a directory that does not exist
        using var model = new FixtureEncoderModel(MissingDirectory(), spec => spec with { HeadResourceName = null });

        // Act: classify a text that needs no encoder
        var results = await model.ClassifyAsync(["the"], TestContext.Current.CancellationToken);

        // Assert: the model answers and has no head
        Assert.Single(results);
        Assert.Null(model.GetHead());
    }

    /// <summary>
    ///     A missing head resource and a head for another encoder file are reported as
    ///     <see cref="ModelIntegrityException"/> with the original exception as the inner exception.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_DamagedOrMismatchedHead_ThrowsModelIntegrity()
    {
        // Arrange: the head resource is missing; the pinned encoder digest differs
        using var missing = new FixtureEncoderModel(MissingDirectory(), spec => spec with { HeadResourceName = "no.such.head.bin" });
        using var digest = new FixtureEncoderModel(MissingDirectory(), spec => spec with
        {
            Files = [.. spec.Files.Select(f => f.Name == spec.OnnxFileName ? f with { Sha256 = new string('a', 64) } : f)],
        });

        // Act
        var first = await Assert.ThrowsAsync<ModelIntegrityException>(() => missing.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<ModelIntegrityException>(() => digest.ClassifyAsync(["the"], TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidDataException>(first.InnerException);
        Assert.Contains("head", first.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(second.InnerException);
        Assert.Contains("another encoder file", second.InnerException.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A model that uses its tense head and whose head file has none is reported as
    ///     <see cref="ModelIntegrityException"/> with the original exception as the inner exception, on every call.
    /// </summary>
    /// <returns>A task that completes when the check has run.</returns>
    [Fact]
    public async Task OnnxWordSenseModel_ClassifyAsync_TenseHeadInUseWithoutTenseHead_ThrowsModelIntegrity()
    {
        // Arrange: the embedded head of the fixture has no tense section
        using var model = new FixtureEncoderModel(MissingDirectory(), spec => spec with { TenseHeadInUse = true });

        // Act
        var first = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        var second = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));

        // Assert
        Assert.IsType<InvalidDataException>(first.InnerException);
        Assert.Contains("no tense head", first.InnerException.Message, StringComparison.Ordinal);
        Assert.IsType<InvalidDataException>(second.InnerException);
    }

    /// <summary>
    ///     The encoder model has the downloadable capability, so the explicit ensure is available through the interface.
    /// </summary>
    [Fact]
    public void OnnxWordSenseModel_IsDownloadable()
    {
        // Arrange
        using var model = Open();

        // Act
        IWordSenseModel contract = model;

        // Assert
        Assert.IsType<IDownloadableWordSenseModel>(contract, exactMatch: false);
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

            await Assert.ThrowsAsync<ModelUnavailableException>(() => model.ClassifyAsync(PendingSentence, TestContext.Current.CancellationToken));
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
    public async Task OnnxWordSenseModel_ClassifyAsync_WeightsOfAnotherImage_ThrowsModelIntegrity()
    {
        // Arrange: a lemma whose second sense does not exist in the image
        var ids = new[] { Fixtures.SenseIds("just")[0], "just#no-such-sense" };
        var lemma = new WeightedLemma("just", ids, Fixtures.Identity(2, FixtureEncoderModel.HiddenDimension, Fixtures.Scale, 1f, LemmaWeights.Never));
        using var model = new FixtureEncoderModel(MissingDirectory(), lemma);

        // Act / Assert: the weights are checked before any model file is needed
        var failure = await Assert.ThrowsAsync<ModelIntegrityException>(() => model.ClassifyAsync(["the"], TestContext.Current.CancellationToken));
        Assert.IsType<InvalidDataException>(failure.InnerException);
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
            var mirror = new DownloadOptions(new Uri(server.BaseUri, "/mirror/"), StoreRoot: store);

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
