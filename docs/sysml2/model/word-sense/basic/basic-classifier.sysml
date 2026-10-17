package WordSense {
    part def BasicClassifier {
        doc /* The classifier of the basic model: looks each word up in the lexicon and, for a word with several senses, applies the rules to its neighbors. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Basic/BasicClassifier.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs */
        comment designRef /* Design: docs/design/word-sense/basic/basic-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/basic/basic-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/basic/basic-classifier.yaml */
    }
}
