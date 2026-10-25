package WordSense {
    part def WordLexicon {
        doc /* The embedded word lexicon and sense catalog: every dictionary word (determiners, conjunctions and interjections included; the spellings without a letter, such as numerals, are left out, with their entries and senses) and every build-time inflection as a spelling with its possible analyses (lemma, part of speech and features; a modal or primary auxiliary ends with an Auxiliary analysis), and every OEWN and project sense of those words with its readable id, group (when it is in one; a group id has its own lemma#group:label namespace), OEWN key, looked up by id, by key or by lemma and part of speech, plus a version string that identifies the sense inventory, and the enumeration of every word key that any model can return (deferred, one pass over the spellings and then streamed; for each normalized spelling and analysis, the key without a label and one key for each catalog sense of its lemma and part of speech); a missing or corrupt embedded image is reported as ModelIntegrityException; read in place from the embedded flat word image wordsense.img (format WSI1 version 5, which also carries the WordNet 3.0 tag count of each sense outside the inventory hash, the usage prior of the senses that no model scores). */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordLexicon.cs, src/DemaConsulting.WordSense/WordImage.cs, src/DemaConsulting.WordSense/WordImage.Catalog.cs, src/DemaConsulting.WordSense/WordImage.Keys.cs, src/DemaConsulting.WordSense/ImageFormat.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/DemaConsulting.WordSense.Tests/WordKeyTests.cs, test/DemaConsulting.WordSense.Tests/WordImageCatalogTests.cs, test/WordSense.Builder.Tests/ImageTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/word-lexicon.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/word-lexicon.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/word-lexicon.yaml */
    }
}
