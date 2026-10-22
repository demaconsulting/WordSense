using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Checks that a model folder holds every pinned file of a model, for the tests that need the real model.
/// </summary>
/// <remarks>
///     This file is compiled into the test projects of the Onnx package, of both encoder models and of the builder, so
///     the <c>ModelFolder</c> helpers apply one rule. A folder that is missing, holds a Git LFS pointer or a file of the
///     wrong size is a checkout without the model content: the test is skipped, which keeps a local run without Git LFS
///     convenient. When the environment variable <c>WORDSENSE_REQUIRE_MODELS</c> is <c>true</c> or <c>1</c> (the CI
///     acceptance run sets it) the same condition fails the test instead, so that a model test can never be skipped
///     silently where the model is expected. A file of the right size with the wrong SHA-256
///     is corrupt: the test fails, because a damaged model must not be skipped silently. The SHA-256 of a folder is checked once per process.
/// </remarks>
internal static class ModelFileCheck
{
    /// <summary>
    ///     The name of the environment variable that turns the skip of a model test into a failure.
    /// </summary>
    public const string RequireModelsVariable = "WORDSENSE_REQUIRE_MODELS";

    private const string PointerPrefix = "version https://git-lfs";

    private static readonly ConcurrentDictionary<string, string> Verified = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    ///     Determines whether the environment demands the model files, so that a model test must not be skipped.
    /// </summary>
    /// <returns>True when <c>WORDSENSE_REQUIRE_MODELS</c> is set to <c>true</c> or <c>1</c>.</returns>
    public static bool IsRequired() => IsRequired(Environment.GetEnvironmentVariable(RequireModelsVariable));

    /// <summary>
    ///     Determines whether the value of <c>WORDSENSE_REQUIRE_MODELS</c> demands the model files.
    /// </summary>
    /// <param name="value">The value of the variable, or null when it is not set.</param>
    /// <returns>True for <c>true</c> (any case) or <c>1</c>; false for anything else.</returns>
    public static bool IsRequired(string? value) =>
        string.Equals(value?.Trim(), "true", StringComparison.OrdinalIgnoreCase) || string.Equals(value?.Trim(), "1", StringComparison.Ordinal);

    /// <summary>
    ///     Ends a test that cannot run because the model files are unavailable: skips it, or fails it when the
    ///     environment demands the model files.
    /// </summary>
    /// <param name="reason">Why the model is unavailable.</param>
    [DoesNotReturn]
    public static void Unavailable(string reason) => Unavailable(reason, IsRequired());

    /// <summary>
    ///     Ends a test that cannot run because the model files are unavailable: skips it, or fails it when the files
    ///     are required.
    /// </summary>
    /// <param name="reason">Why the model is unavailable.</param>
    /// <param name="required">True to fail the test, false to skip it.</param>
    [DoesNotReturn]
    public static void Unavailable(string reason, bool required)
    {
        if (required)
        {
            Assert.Fail($"{reason} The model files are required because {RequireModelsVariable} is set; fetch them with 'git lfs pull'.");
        }

        Assert.Skip(reason);
    }

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
    ///     Gets the folder when it holds every pinned file, or skips the calling test (fails it when the files are required).
    /// </summary>
    /// <param name="spec">The model with the pinned files.</param>
    /// <param name="directory">The folder.</param>
    /// <returns>The folder.</returns>
    public static string Require(ModelSpec spec, string directory)
    {
        if (FindMissing(spec, directory) is { } reason)
        {
            Unavailable(reason);
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
