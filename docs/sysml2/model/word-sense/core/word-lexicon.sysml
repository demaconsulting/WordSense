package WordSense {
    part def WordLexicon {
        doc /* The embedded word lexicon: every dictionary word (determiners, conjunctions and interjections included) and every build-time inflection, as a lower-case surface word with its possible senses (a modal or primary auxiliary ends with an Auxiliary sense), read from the embedded words.bin and exposed as a read-only dictionary of immutable sense lists. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordLexicon.cs, src/DemaConsulting.WordSense/WordsBlob.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/DemaConsulting.WordSense.Tests/WordsBlobTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-lexicon.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-lexicon.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-lexicon.yaml */
    }
}
