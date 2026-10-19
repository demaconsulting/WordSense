package WordSense {
    part def OewnImporter {
        doc /* One-time, deterministic import of the pinned Open English WordNet LMF file into the tables, with the derived columns, the single stored direction of relations and the safety checks, and the import of the pinned WordNet 3.0 sense counts into the sense frequency table. */

        comment sourceRef /* Source: tools/WordSense.Builder/Data/OewnImporter.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/Data/OewnImporterTests.cs, test/WordSense.Builder.Tests/Data/DataEndToEndTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/oewn-importer.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/oewn-importer.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/oewn-importer.yaml */
    }
}
