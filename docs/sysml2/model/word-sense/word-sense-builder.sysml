package WordSense {
    part def WordSenseBuilder {
        doc /* Non-shipped build-time tool that derives the dictionary word list from the pinned Moby list, builds the embedded word lexicon from the corpus with the generated inflected forms of every dictionary word, measures the token coverage of the lexicon on plain-text files, trains the weights of each encoder model, counts Universal Dependencies tag statistics, trains, tunes and measures the statistical basic model, measures the shipped models, and generates the senses appendix of the user guide. */

        comment sourceRef /* Source: tools/WordSense.Builder/ (every C# file of the builder project, for example Program.cs, WordsBuilder.cs, MorphologyExpander.cs, IrregularForms.cs, CoverageReport.cs, LexiconImporter.cs, ModelTrainer.cs, WeightsTrainer.cs, Evaluator.cs, UdCounts.cs, BasicHyperparameters.cs, BasicTrainer.cs, BasicTablesWriter.cs, BasicTuner.cs and BasicModelBuilder.cs), corpus/ (sense inventory, anchor, holdout and form sentences, lexicon, Universal Dependencies counts, provenance) */
        comment testRef /* Test: test/WordSense.Builder.Tests/ProgramTests.cs, test/WordSense.Builder.Tests/WordsBlobWriterTests.cs, test/WordSense.Builder.Tests/WeightsBlobWriterTests.cs, test/WordSense.Builder.Tests/UdCountsTests.cs, test/WordSense.Builder.Tests/BasicTrainerTests.cs, test/WordSense.Builder.Tests/BuilderTests.cs, test/WordSense.Builder.Tests/LexiconTests.cs, test/WordSense.Builder.Tests/LexiconImporterTests.cs, test/WordSense.Builder.Tests/WeightsTrainerTests.cs, test/WordSense.Builder.Tests/WordsBuilderTests.cs, test/WordSense.Builder.Tests/CoverageReportTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-builder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-builder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-builder.yaml */
    }
}
