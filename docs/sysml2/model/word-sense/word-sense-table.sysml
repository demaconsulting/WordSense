package WordSense {
    part def WordSenseTable {
        doc /* The embedded, integrity-checked vocabulary of lemma entries (senses with part of speech and optional pronunciation, optional per-lemma linear classifier weights), word forms and dictionary words, with the single resolver that maps a surface word to a lemma entry. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordSenseTable.cs, src/DemaConsulting.WordSense/LemmaEntry.cs, src/DemaConsulting.WordSense/SenseWeights.cs, src/DemaConsulting.WordSense/LexicalIndex.cs, src/DemaConsulting.WordSense/LemmaMorphology.cs, src/DemaConsulting.WordSense/LemmaResolution.cs, src/DemaConsulting.WordSense/VectorMath.cs, src/DemaConsulting.WordSense/WordSenseTableFormatException.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseTableTests.cs, test/DemaConsulting.WordSense.Tests/LexicalIndexTests.cs, test/DemaConsulting.WordSense.Tests/LemmaResolutionTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-table.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-table.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-table.yaml */
    }
}
