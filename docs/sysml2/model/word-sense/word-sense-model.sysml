package WordSense {
    part def WordSenseModel {
        doc /* The abstraction of an encoder model (identity, digest, dimension and contextual encoding) and the check that rejects a weights table trained for another model. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/IWordSenseModel.cs, src/DemaConsulting.WordSense/WordSenseModelMismatchException.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseClassifierTests.cs, test/DemaConsulting.WordSense.Tests/WordSenseWeightsTableTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-model.yaml */
    }
}
