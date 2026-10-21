package WordSense {
    part def WordLexicon {
        doc /* The embedded word lexicon and sense catalog: every dictionary word (determiners, conjunctions and interjections included) and every build-time inflection as a spelling with its possible analyses (lemma, part of speech and features; a modal or primary auxiliary ends with an Auxiliary analysis), and every OEWN and project sense with its readable id, group (when it is in one; a group id has its own lemma#group:label namespace), OEWN key and respelling, looked up by id, by key or by lemma and part of speech, plus a version string that identifies the sense inventory; read in place from the embedded flat word image wordsense.img (format WSI1 version 3). */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordLexicon.cs, src/DemaConsulting.WordSense/WordImage.cs, src/DemaConsulting.WordSense/ImageFormat.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/WordSense.Builder.Tests/ImageTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-lexicon.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-lexicon.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-lexicon.yaml */
    }
}
