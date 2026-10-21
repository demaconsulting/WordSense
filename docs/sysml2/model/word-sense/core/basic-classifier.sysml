package WordSense {
    part def BasicClassifier {
        doc /* The classifier of the basic model: looks each word up in the lexicon and, for a word with several analyses, fuses part-of-speech posteriors and context cues into a probability for every analysis, the analyses of one lemma and part of speech (a slot) sharing the probability of the slot, selecting the best analysis of the most probable slot; it answers at the part of speech level with the probability of the slot as confidence (the sense level where the selected analysis has one sense), never at the group level, and lists senses only for an analysis whose lemma and part of speech have exactly one sense. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/BasicClassifier.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/BasicClassifierTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/basic-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/basic-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/basic-classifier.yaml */
    }
}
