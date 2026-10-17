package WordSense {
    part def CoreSubsystem {
        doc /* The core package DemaConsulting.WordSense: the embedded word lexicon and the contracts every model package implements. It has no vectors, weights, ONNX Runtime or other third-party dependency. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/DemaConsulting.WordSense.Tests/ContractsTests.cs, test/DemaConsulting.WordSense.Tests/WordsBlobTests.cs, test/DemaConsulting.WordSense.Tests/ReferenceTokenizerTests.cs, test/DemaConsulting.WordSense.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/core.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core.yaml */

        part wordLexicon : WordLexicon;
        part wordSenseContracts : WordSenseContracts;
    }
}
