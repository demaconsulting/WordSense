package WordSense {
    part def MiniLMSubsystem {
        doc /* The MiniLM package DemaConsulting.WordSense.MiniLM: the all-MiniLM-L6-v2 int8 encoder (uncased) as a word-sense model, with the weights trained for it embedded in the assembly. */

        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/MiniLMWordSenseModelTests.cs, test/DemaConsulting.WordSense.MiniLM.Tests/EndToEndTests.cs, test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSampleTests.cs, test/DemaConsulting.WordSense.MiniLM.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/mini-lm.md */
        comment verificationRef /* Verification: docs/verification/word-sense/mini-lm.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/mini-lm.yaml */

        part miniLMWordSenseModel : MiniLMWordSenseModel;
    }
}
