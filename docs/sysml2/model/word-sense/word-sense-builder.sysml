package WordSense {
    part def WordSenseBuilder {
        doc /* Non-shipped build-time tool that trains the classifier table from the labeled corpus, imports sentence batches, runs the part-of-speech audit and relabeling, and restores and evaluates senses with real, provenance-tracked sentences, and generates the senses appendix of the user guide. */

        comment sourceRef /* Source: tools/WordSense.Builder/Program.cs, tools/WordSense.Builder/AppendixGenerator.cs, tools/WordSense.Builder/NounVerbImport.cs, tools/WordSense.Builder/NounVerbGate.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/BuilderTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-builder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-builder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-builder.yaml */
    }
}
