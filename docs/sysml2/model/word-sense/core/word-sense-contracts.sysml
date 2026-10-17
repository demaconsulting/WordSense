package WordSense {
    part def WordSenseContracts {
        doc /* The public contracts shared by every model package: the disposable model interface (the model owns the lifetime and classifies, ensuring its files lazily), the mirror options, and the answer values (word sense, word details and part of speech). */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/IWordSenseModel.cs, src/DemaConsulting.WordSense/WordDetails.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/ContractsTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-sense-contracts.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-sense-contracts.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-sense-contracts.yaml */
    }
}
