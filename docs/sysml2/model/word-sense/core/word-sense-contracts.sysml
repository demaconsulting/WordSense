package WordSense {
    part def WordSenseContracts {
        doc /* The public contracts shared by every model package: the disposable model interface (the model owns the lifetime and classifies, ensuring its files lazily), the composite FallbackWordSenseModel that answers each word with the result of a primary model where it scored senses and otherwise with the result of a fallback model, the mirror options, the answer values (classification result with its ranked analyses, each with its ranked senses; answer level; word details as one analysis and part of speech) and the sense and group records of the catalog. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/IWordSenseModel.cs, src/DemaConsulting.WordSense/FallbackWordSenseModel.cs, src/DemaConsulting.WordSense/ClassifyResult.cs, src/DemaConsulting.WordSense/AnalysisCandidate.cs, src/DemaConsulting.WordSense/SenseCandidate.cs, src/DemaConsulting.WordSense/WordDetails.cs, src/DemaConsulting.WordSense/SenseInfo.cs, src/DemaConsulting.WordSense/SenseLevel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/ContractsTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-sense-contracts.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-sense-contracts.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-sense-contracts.yaml */
    }
}
