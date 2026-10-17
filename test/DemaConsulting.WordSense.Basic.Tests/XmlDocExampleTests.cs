namespace DemaConsulting.WordSense.Basic.Tests;

/// <summary>
///     Tests that every example documented on the Basic package's public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file of the Basic package compiles.
    /// </summary>
    [Fact]
    public void BasicWordSense_XmlDocExamples_Compile()
    {
        DemaConsulting.WordSense.Tests.XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.WordSense.Basic.xml", "DemaConsulting.WordSense", "DemaConsulting.WordSense.Basic");
    }
}
