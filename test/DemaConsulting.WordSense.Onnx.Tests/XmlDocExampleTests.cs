namespace DemaConsulting.WordSense.Onnx.Tests;

/// <summary>
///     Tests that every example documented on the Onnx package's public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file of the Onnx package compiles.
    /// </summary>
    [Fact]
    public void OnnxWordSense_XmlDocExamples_Compile()
    {
        DemaConsulting.WordSense.Tests.XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.WordSense.Onnx.xml", "DemaConsulting.WordSense", "DemaConsulting.WordSense.Onnx");
    }
}
