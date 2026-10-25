package WordSense {
    part def PosTagger {
        doc /* The part-of-speech tagger of the basic model: a sparse forward-backward second-order hidden Markov model giving per-word part-of-speech posteriors for a sentence segment. The state carries one extra bit, whether the current clause (it starts at the segment start and after a conjunction or punctuation) already has a verb, and a possessive written as one token is read as its stem and the possessive marker while the results stay one for each caller token. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/PosTagger.cs, src/DemaConsulting.WordSense/PosTag.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/PosTaggerTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/pos-tagger.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/pos-tagger.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/pos-tagger.yaml */
    }
}
