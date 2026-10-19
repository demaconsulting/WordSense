package WordSense {
    part def SenseLabelGenerator {
        doc /* Generates the readable, deterministic label of every sense that has no curated label (lemma#noun for the only sense of a lemma and part of speech, otherwise the first synonym of its synset or the head lemma of its hypernym, made unique by a numeric suffix), never changes or reuses a published label, and writes the sense label table. */

        comment sourceRef /* Source: tools/WordSense.Builder/Data/SenseLabelGenerator.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/Data/SenseLabelGeneratorTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/sense-label-generator.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/sense-label-generator.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/sense-label-generator.yaml */
    }
}
