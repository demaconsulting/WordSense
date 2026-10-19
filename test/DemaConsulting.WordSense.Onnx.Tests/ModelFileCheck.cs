using System.Collections.Concurrent;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Checks that a model folder holds every pinned file of a model, for the tests that need the real model.
/// </summary>
/// <remarks>
///     This file is compiled into the test projects of the Onnx package and of both encoder models, so the three
///     <c>ModelFolder</c> helpers apply one rule. A folder that is missing, holds a Git LFS pointer or a file of the wrong
///     size is a checkout without the model content: the test is skipped. A file of the right size with the wrong SHA-256
///     is corrupt: the test fails, because a damaged model must not be skipped silently. The SHA-256 of a folder is checked once per process.
/// </remarks>
internal static class ModelFileCheck
{
    private const string PointerPrefix = "version https://git-lfs";

    private static readonly ConcurrentDictionary<string, string> Verified = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Looks for a reason why a folder cannot serve as the model folder.
    /// </summary>
    /// <param name="spec">The model with the pinned files.</param>
    /// <param name="directory">The folder.</param>
    /// <returns>Null when every file is present, real and of the pinned size; otherwise the reason, naming the file.</returns>
    public static string? FindMissing(ModelSpec spec, string directory)
    {
        ArgumentNullException.ThrowIfNull(spec);
        foreach (var file in spec.Files)
        {
            var path = Path.Join(directory, file.Name);
            if (!File.Exists(path))
            {
                return $"The model file '{file.Name}' is missing.";
            }

            if (IsLfsPointer(path))
            {
                return $"The model file '{file.Name}' is a Git LFS pointer; run 'git lfs pull'.";
            }

            if (file.Length > 0 && new FileInfo(path).Length != file.Length)
            {
                return $"The model file '{file.Name}' does not have the pinned size of {file.Length} bytes.";
            }
        }

        return null;
    }

    /// <summary>
    ///     Gets the folder when it holds every pinned file, or skips the calling test.
    /// </summary>
    /// <param name="spec">The model with the pinned files.</param>
    /// <param name="directory">The folder.</param>
    /// <returns>The folder.</returns>
    public static string Require(ModelSpec spec, string directory)
    {
        if (FindMissing(spec, directory) is { } reason)
        {
            Assert.Skip(reason);
        }

        // A file of the right size that fails its digest is damaged, not absent: fail instead of skipping
        // ConcurrentDictionary rejects null values, so an empty string stands for "no corruption found"
        var corrupt = Verified.GetOrAdd(directory, d => FindCorrupt(spec, d) ?? string.Empty);
        if (corrupt.Length > 0)
        {
            Assert.Fail(corrupt);
        }

        return directory;
    }

    /// <summary>
    ///     Determines whether a file is a Git LFS pointer rather than real content.
    /// </summary>
    /// <param name="path">The file path.</param>
    /// <returns>True for a pointer file.</returns>
    public static bool IsLfsPointer(string path)
    {
        using var stream = File.OpenRead(path);
        var buffer = new byte[64];
        var read = stream.Read(buffer, 0, buffer.Length);
        return Encoding.ASCII.GetString(buffer, 0, read).StartsWith(PointerPrefix, StringComparison.Ordinal);
    }

    private static string? FindCorrupt(ModelSpec spec, string directory)
    {
        foreach (var file in spec.Files)
        {
            if (!string.Equals(ModelDownloader.HashFile(Path.Join(directory, file.Name)), file.Sha256, StringComparison.Ordinal))
            {
                return $"The model file '{file.Name}' in '{directory}' does not match its pinned SHA-256.";
            }
        }

        return null;
    }
}
