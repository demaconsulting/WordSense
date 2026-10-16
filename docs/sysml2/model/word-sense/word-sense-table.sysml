package WordSense {
    part def WordSenseTable {
        doc /* The embedded, integrity-checked table of per-lemma linear classifiers, sense definitions (part of speech, meaning, respelling) and word forms. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordSenseTable.cs, src/DemaConsulting.WordSense/LemmaModel.cs, src/DemaConsulting.WordSense/VectorMath.cs, src/DemaConsulting.WordSense/WordSenseTableFormatException.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseTableTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-table.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-table.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-table.yaml */
    }
}
