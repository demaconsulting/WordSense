namespace DemaConsulting.WordSense.BertBaseCased.Tests;

/// <summary>
///     Tests that every example documented on the BertBaseCased package's public API compiles against the real API.
/// </summary>
public class XmlDocExampleTests
{
    /// <summary>
    ///     Every documented example in the shipped XML documentation file of the BertBaseCased package compiles.
    /// </summary>
    [Fact]
    public void BertBaseCasedWordSense_XmlDocExamples_Compile()
    {
        DemaConsulting.WordSense.Tests.XmlDocExampleVerifier.VerifyExamples(
            "DemaConsulting.WordSense.BertBaseCased.xml", "DemaConsulting.WordSense", "DemaConsulting.WordSense.BertBaseCased");
    }
}
