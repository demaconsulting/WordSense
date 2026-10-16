namespace DemaConsulting.WordSense.Tests;

/// <summary>
///     Tests that every example documented on the WordSense public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file compiles.
    /// </summary>
    [Fact]
    public void WordSense_XmlDocExamples_Compile()
    {
        XmlDocExampleVerifier.VerifyExamples("DemaConsulting.WordSense.xml", "DemaConsulting.WordSense");
    }
}
