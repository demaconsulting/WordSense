namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests of the package layering of the Onnx assembly.
/// </summary>
public class OnnxLayeringTests
{
    /// <summary>
    ///     The Onnx assembly builds on the core package and on nothing else of the WordSense family: besides
    ///     the framework and the ONNX Runtime it references only <c>DemaConsulting.WordSense</c>, and never the Basic
    ///     package or one of the concrete model packages.
    /// </summary>
    [Fact]
    public void OnnxWordSense_Assembly_ReferencesOnlyCore()
    {
        // Arrange: the names of the assemblies that the Onnx assembly references
        var references = typeof(OnnxWordSenseModel).Assembly.GetReferencedAssemblies()
            .Select(r => r.Name!)
            .ToList();

        // Act: keep the assemblies that are neither the framework nor the ONNX Runtime
        var others = references
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                && !name.StartsWith("Microsoft.", StringComparison.Ordinal)
                && !string.Equals(name, "mscorlib", StringComparison.Ordinal)
                && !string.Equals(name, "netstandard", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        // Assert: exactly the core, and neither the Basic package nor an encoder model package
        Assert.Equal(["DemaConsulting.WordSense"], others);
        Assert.DoesNotContain(references, name => name.Contains("Basic", StringComparison.Ordinal) || name.Contains("MiniLM", StringComparison.Ordinal) || name.Contains("BertBaseCased", StringComparison.Ordinal));
    }
}
