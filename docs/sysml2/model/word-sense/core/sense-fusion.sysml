package WordSense {
    part def SenseFusion {
        doc /* The evidence fusion of the basic model: combines part-of-speech posteriors and cue scores into per-analysis probabilities and chooses the winner with its probability as confidence. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/SenseFusion.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/SenseFusionTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/sense-fusion.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/sense-fusion.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/sense-fusion.yaml */
    }
}
