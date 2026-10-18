package WordSense {
    part def BertBaseCasedSubsystem {
        doc /* The BertBaseCased package DemaConsulting.WordSense.BertBaseCased: the bert-base-cased int8 encoder (cased) as a word-sense model, with the weights trained for it embedded in the assembly. */

        comment testRef /* Test: test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs, test/DemaConsulting.WordSense.BertBaseCased.Tests/EndToEndTests.cs, test/DemaConsulting.WordSense.BertBaseCased.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/bert-base-cased.md */
        comment verificationRef /* Verification: docs/verification/word-sense/bert-base-cased.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/bert-base-cased.yaml */

        part bertBaseCasedWordSenseModel : BertBaseCasedWordSenseModel;
    }
}
