using DemaConsulting.WordSense.MiniLM;

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
        File.WriteAllBytes(Path.Combine(_folder.FullName, "model.bin"), new byte[4]);
        File.WriteAllText(Path.Combine(_folder.FullName, "vocab.txt"), "version https://git-lfs.github.com/spec/v1\noid sha256:00\nsize 4\n");

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
        File.WriteAllBytes(Path.Combine(_folder.FullName, "model.bin"), new byte[3]);
        File.WriteAllBytes(Path.Combine(_folder.FullName, "vocab.txt"), new byte[3]);

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
        File.WriteAllBytes(Path.Combine(_folder.FullName, "model.bin"), new byte[4]);
        File.WriteAllBytes(Path.Combine(_folder.FullName, "vocab.txt"), new byte[4]);

        Assert.Null(ModelFileCheck.FindMissing(Spec(length: 4), _folder.FullName));
    }

    /// <summary>
    ///     A file that starts with the pointer text is a pointer; a binary file is not.
    /// </summary>
    [Fact]
    public void ModelFileCheck_IsLfsPointer_DetectsThePrefix()
    {
        var pointer = Path.Combine(_folder.FullName, "pointer");
        var real = Path.Combine(_folder.FullName, "real");
        File.WriteAllText(pointer, "version https://git-lfs.github.com/spec/v1\n");
        File.WriteAllBytes(real, [1, 2, 3]);

        Assert.True(ModelFileCheck.IsLfsPointer(pointer));
        Assert.False(ModelFileCheck.IsLfsPointer(real));
    }

    private static ModelSpec Spec(long length = 0) => new(
        "test",
        [new ModelFile("model.bin", new string('0', 64), length), new ModelFile("vocab.txt", new string('0', 64), length)],
        "model.bin",
        "vocab.txt",
        true,
        typeof(MiniLMWordSenseModel).Assembly,
        "none",
        4);
}
