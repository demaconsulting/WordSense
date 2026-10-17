package WordSense {
    part def WordLexicon {
        doc /* The embedded word lexicon: every known lower-case surface word, including build-time inflections, with its possible senses, read from the embedded words.bin and exposed as a read-only dictionary. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordLexicon.cs, src/DemaConsulting.WordSense/WordsBlob.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/DemaConsulting.WordSense.Tests/WordsBlobTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-lexicon.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-lexicon.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-lexicon.yaml */
    }
}
