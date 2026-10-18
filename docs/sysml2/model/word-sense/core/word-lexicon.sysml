package WordSense {
    part def WordLexicon {
        doc /* The embedded word lexicon: every dictionary word (determiners, conjunctions and interjections included) and every build-time inflection, as a spelling with its possible analyses (sense id, lemma, part of speech and features; a modal or primary auxiliary ends with an Auxiliary analysis), read in place from the embedded flat word image wordsense.img. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordLexicon.cs, src/DemaConsulting.WordSense/WordImage.cs, src/DemaConsulting.WordSense/ImageFormat.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/WordSense.Builder.Tests/ImageTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-lexicon.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-lexicon.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-lexicon.yaml */
    }
}
