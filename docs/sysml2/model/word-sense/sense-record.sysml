package WordSense {
    part def SenseRecord {
        doc /* Immutable answer values (part of speech, pronunciation, word details, word analysis with its answer basis and candidates), sense definition validation, the abstention rule and text segmentation. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/AnswerBasis.cs, src/DemaConsulting.WordSense/PartOfSpeech.cs, src/DemaConsulting.WordSense/Pronunciation.cs, src/DemaConsulting.WordSense/WordDetails.cs, src/DemaConsulting.WordSense/WordAnalysis.cs, src/DemaConsulting.WordSense/SenseDefinition.cs, src/DemaConsulting.WordSense/AbstainRule.cs, src/DemaConsulting.WordSense/TextSplitter.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/SenseRecordTests.cs */
        comment designRef /* Design: docs/design/word-sense/sense-record.md */
        comment verificationRef /* Verification: docs/verification/word-sense/sense-record.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/sense-record.yaml */
    }
}
