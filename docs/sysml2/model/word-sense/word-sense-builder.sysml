package WordSense {
    part def WordSenseBuilder {
        doc /* Non-shipped build-time tool that builds the embedded word lexicon from the corpus, trains the weights of each encoder model, measures the shipped models and the rules of the basic model, and generates the senses appendix of the user guide. */

        comment sourceRef /* Source: tools/WordSense.Builder/ (every C# file of the builder project, for example Program.cs, WordsBuilder.cs, ModelTrainer.cs, WeightsTrainer.cs, Evaluator.cs and BasicPrecision.cs), corpus/ (sense inventory, anchor, holdout and form sentences, lexicon, provenance) */
        comment testRef /* Test: test/WordSense.Builder.Tests/ProgramTests.cs, test/WordSense.Builder.Tests/WordsBlobWriterTests.cs, test/WordSense.Builder.Tests/WeightsBlobWriterTests.cs, test/WordSense.Builder.Tests/BasicPrecisionTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-builder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-builder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-builder.yaml */
    }
}
