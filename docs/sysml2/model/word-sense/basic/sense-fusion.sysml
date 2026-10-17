package WordSense {
    part def SenseFusion {
        doc /* The evidence fusion of the basic model: combines part-of-speech posteriors and cue scores into per-sense probabilities and chooses the winner with its probability as confidence. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Basic/SenseFusion.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Basic.Tests/SenseFusionTests.cs */
        comment designRef /* Design: docs/design/word-sense/basic/sense-fusion.md */
        comment verificationRef /* Verification: docs/verification/word-sense/basic/sense-fusion.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/basic/sense-fusion.yaml */
    }
}
