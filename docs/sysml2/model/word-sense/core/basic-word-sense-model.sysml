package WordSense {
    part def BasicWordSenseModel {
        doc /* The public statistical word-sense model: it needs no files, performs no download, offers no preparation step (it does not implement IDownloadableWordSenseModel) and classifies with the basic classifier; a damaged embedded table is reported as ModelIntegrityException, and its disposal only marks the model so that later use throws. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/BasicWordSenseModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/BasicWordSenseModelTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/basic-word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/basic-word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/basic-word-sense-model.yaml */
    }
}
