using DemaConsulting.WordSense.MiniLM;
using Xunit.Sdk;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests of the model folder check that the encoder tests use to decide between running, skipping and failing.
/// </summary>
public sealed class ModelFileCheckTests : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("ws-check-");

    /// <inheritdoc />
    public void Dispose() => _folder.Delete(true);

    /// <summary>
    ///     A folder without the pinned files is reported with the name of the first missing file.
    /// </summary>
    [Fact]
    public void ModelFileCheck_FindMissing_EmptyFolder_NamesTheFile()
    {
        var reason = ModelFileCheck.FindMissing(Spec(), _folder.FullName);

        Assert.NotNull(reason);
        Assert.Contains("model.bin", reason, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A Git LFS pointer in place of a real file is reported, whatever the other files are.
    /// </summary>
    [Fact]
    public void ModelFileCheck_FindMissing_LfsPointer_IsReported()
    {
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), new byte[4]);
        File.WriteAllText(Path.Join(_folder.FullName, "vocab.txt"), "version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 4\n");

        var reason = ModelFileCheck.FindMissing(Spec(length: 0), _folder.FullName);

        Assert.NotNull(reason);
        Assert.Contains("vocab.txt", reason, StringComparison.Ordinal);
        Assert.Contains("LFS", reason, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A file of another size than the pinned one is reported.
    /// </summary>
    [Fact]
    public void ModelFileCheck_FindMissing_WrongSize_IsReported()
    {
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), new byte[3]);
        File.WriteAllBytes(Path.Join(_folder.FullName, "vocab.txt"), new byte[3]);

        var reason = ModelFileCheck.FindMissing(Spec(length: 4), _folder.FullName);

        Assert.NotNull(reason);
        Assert.Contains("size", reason, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A folder with every file at its pinned size passes the presence check.
    /// </summary>
    [Fact]
    public void ModelFileCheck_FindMissing_CompleteFolder_IsNull()
    {
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), new byte[4]);
        File.WriteAllBytes(Path.Join(_folder.FullName, "vocab.txt"), new byte[4]);

        Assert.Null(ModelFileCheck.FindMissing(Spec(length: 4), _folder.FullName));
    }

    /// <summary>
    ///     A file that starts with the pointer text is a pointer; a binary file is not.
    /// </summary>
    [Fact]
    public void ModelFileCheck_IsLfsPointer_DetectsThePrefix()
    {
        var pointer = Path.Join(_folder.FullName, "pointer");
        var real = Path.Join(_folder.FullName, "real");
        File.WriteAllText(pointer, "version https://git-lfs.github.com/spec/v1\n");
        File.WriteAllBytes(real, [1, 2, 3]);

        Assert.True(ModelFileCheck.IsLfsPointer(pointer));
        Assert.False(ModelFileCheck.IsLfsPointer(real));
    }

    /// <summary>
    ///     A complete folder whose files match their pinned digests is returned without any exception.
    /// </summary>
    [Fact]
    public void ModelFileCheck_Require_ValidFolder_ReturnsFolderWithoutThrowing()
    {
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Join(_folder.FullName, "vocab.txt"), [5, 6, 7, 8]);
        var spec = Spec(length: 4, model: HashOf("model.bin"), vocab: HashOf("vocab.txt"));

        Assert.Equal(_folder.FullName, ModelFileCheck.Require(spec, _folder.FullName));
        Assert.Equal(_folder.FullName, ModelFileCheck.Require(spec, _folder.FullName));
    }

    /// <summary>
    ///     A folder whose file has the pinned size but the wrong digest fails the test instead of throwing a cache error.
    /// </summary>
    [Fact]
    public void ModelFileCheck_Require_CorruptFolder_FailsWithDigestMessage()
    {
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), [1, 2, 3, 4]);
        File.WriteAllBytes(Path.Join(_folder.FullName, "vocab.txt"), [5, 6, 7, 8]);

        var error = Record.Exception(() => ModelFileCheck.Require(Spec(length: 4), _folder.FullName));

        Assert.NotNull(error);
        Assert.Contains("SHA-256", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Only <c>true</c> (any case, surrounding blanks allowed) and <c>1</c> demand the model files.
    /// </summary>
    /// <param name="value">The value of the variable.</param>
    /// <param name="expected">Whether the model files are required.</param>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    [InlineData("yes", false)]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData(" TRUE ", true)]
    [InlineData("1", true)]
    public void ModelFileCheck_IsRequired_Value_IsParsed(string? value, bool expected)
    {
        // Arrange: the value of WORDSENSE_REQUIRE_MODELS

        // Act
        var required = ModelFileCheck.IsRequired(value);

        // Assert
        Assert.Equal(expected, required);
    }

    /// <summary>
    ///     When the model files are required, an unavailable model fails the test and names the variable and the reason.
    /// </summary>
    [Fact]
    public void ModelFileCheck_Unavailable_Required_FailsWithTheReason()
    {
        // Arrange: the files are required, as in the CI acceptance run

        // Act
        var error = Record.Exception(() => ModelFileCheck.Unavailable("The model file 'model.bin' is missing.", true));

        // Assert: a failure, not a skip
        Assert.NotNull(error);
        Assert.IsType<FailException>(error);
        Assert.Contains("model.bin", error.Message, StringComparison.Ordinal);
        Assert.Contains(ModelFileCheck.RequireModelsVariable, error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     When the model files are not required, an unavailable model skips the test and does not fail it.
    /// </summary>
    [Fact]
    public void ModelFileCheck_Unavailable_NotRequired_SkipsInsteadOfFailing()
    {
        // Arrange: a local run without the model files
        Exception? error = null;

        // Act: Record.Exception does not catch the exception that skips a test, so it is caught here
        try
        {
            ModelFileCheck.Unavailable("The model file 'model.bin' is missing.", false);
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Assert: an exception that skips the test, not the exception of a failed assertion
        Assert.NotNull(error);
        Assert.IsNotType<FailException>(error);
        Assert.Contains("model.bin", error.Message, StringComparison.Ordinal);
    }

    /// <summary>
    ///     A folder that holds a Git LFS pointer is reported with the pointer reason when the files are required.
    /// </summary>
    [Fact]
    public void ModelFileCheck_Unavailable_LfsPointerRequired_FailsNamingThePointer()
    {
        // Arrange: a pointer in place of the vocabulary
        File.WriteAllBytes(Path.Join(_folder.FullName, "model.bin"), new byte[4]);
        File.WriteAllText(Path.Join(_folder.FullName, "vocab.txt"), "version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 4\n");
        var reason = ModelFileCheck.FindMissing(Spec(length: 0), _folder.FullName);

        // Act
        var error = Record.Exception(() => ModelFileCheck.Unavailable(reason!, true));

        // Assert
        Assert.IsType<FailException>(error);
        Assert.Contains("Git LFS pointer", error.Message, StringComparison.Ordinal);
    }

    private string HashOf(string name) => ModelDownloader.HashFile(Path.Join(_folder.FullName, name));

    private static ModelSpec Spec(long length = 0, string? model = null, string? vocab = null) => new(
        "test",
        [new ModelFile("model.bin", model ?? new string('0', 64), length), new ModelFile("vocab.txt", vocab ?? new string('0', 64), length)],
        "model.bin",
        "vocab.txt",
        true,
        typeof(MiniLMWordSenseModel).Assembly,
        "none",
        4);
}
