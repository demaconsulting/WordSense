package WordSense {
    part def WordSenseBuilder {
        doc /* Non-shipped build-time tool that builds the lexicon table, trains one weights table per encoder model from the shared labeled corpus, imports sentence batches, validates the sense inventory (parts of speech, meanings and respellings), reports accuracy per level, restores and evaluates senses with real, provenance-tracked sentences, generates the senses appendix of the user guide, imports the public-domain Moby part-of-speech list into the committed derived lexicon, embeds that lexicon in the table and evaluates it against a precision gate. */

        comment sourceRef /* Source: tools/WordSense.Builder/ (every C# file of the builder project, for example Program.cs, TableBuilder.cs, LogisticTrainer.cs, LexiconSource.cs and LexiconEvaluation.cs), corpus/lexicon/ (lexicon.tsv, corrections.tsv, classes.tsv, gated.tsv, morphology.tsv, PROVENANCE.md) */
        comment testRef /* Test: test/WordSense.Builder.Tests/BuilderTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-builder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-builder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-builder.yaml */
    }
}
