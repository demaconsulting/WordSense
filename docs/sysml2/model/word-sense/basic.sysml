package WordSense {
    part def BasicSubsystem {
        doc /* The Basic package DemaConsulting.WordSense.Basic: a word-sense model that needs no model files, weights or network, and chooses the part of speech of an ambiguous word with hand-written rules over its neighbors. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Basic.Tests/BasicWordSenseModelTests.cs, test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs, test/DemaConsulting.WordSense.Basic.Tests/RulesTests.cs */
        comment designRef /* Design: docs/design/word-sense/basic.md */
        comment verificationRef /* Verification: docs/verification/word-sense/basic.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/basic.yaml */

        part basicWordSenseModel : BasicWordSenseModel;
        part basicClassifier : BasicClassifier;
        part rules : Rules;
    }
}
