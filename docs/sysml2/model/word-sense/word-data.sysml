package WordSense {
    part def WordDataSubsystem {
        doc /* The word data: the TSV tables under data/ (imported from Open English WordNet, CC BY 4.0), their schema, and the build-time builder code that imports and validates them. Data only; nothing here is shipped in a package. */

        comment testRef /* Test: test/WordSense.Builder.Tests/Data/DataEndToEndTests.cs, test/WordSense.Builder.Tests/Data/DataSchemaTests.cs, test/WordSense.Builder.Tests/Data/DataLayoutTests.cs, test/WordSense.Builder.Tests/Data/OewnImporterTests.cs, test/WordSense.Builder.Tests/Data/DataValidatorTests.cs, test/WordSense.Builder.Tests/Data/FormGeneratorDataTests.cs, test/WordSense.Builder.Tests/Data/InflectionRulesTests.cs, test/WordSense.Builder.Tests/Data/FormAuditorTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data.yaml */

        part dataSchema : DataSchema;
        part dataLayout : DataLayout;
        part oewnImporter : OewnImporter;
        part formGenerator : FormGenerator;
        part formAuditor : FormAuditor;
        part dataValidator : DataValidator;
    }
}
