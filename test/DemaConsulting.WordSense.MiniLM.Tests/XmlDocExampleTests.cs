namespace DemaConsulting.WordSense.MiniLM.Tests;

/// <summary>
///     Tests that every example documented on the MiniLM package's public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file of the MiniLM package compiles.
    /// </summary>
    [Fact]
    public void MiniLMWordSense_XmlDocExamples_Compile()
    {
        DemaConsulting.WordSense.Tests.XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.WordSense.MiniLM.xml", "DemaConsulting.WordSense", "DemaConsulting.WordSense.MiniLM");
    }
}
