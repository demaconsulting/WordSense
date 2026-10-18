package WordSense {
    part def FormGenerator {
        doc /* Generates the inflected-form table: the spellings of the dictionary entries and their analyses, each row with its source, keeping a generated row only when two independent sources support it. */

        comment sourceRef /* Source:         tools/WordSense.Builder/Data/FormGenerator.cs, tools/WordSense.Builder/Data/FactGenerator.cs, tools/WordSense.Builder/Data/InflectionRules.cs, tools/WordSense.Builder/Data/AgidImporter.cs, tools/WordSense.Builder/Data/IrregularSources.cs */
                comment testRef /* Test: test/WordSense.Builder.Tests/Data/FormGeneratorDataTests.cs, test/WordSense.Builder.Tests/Data/FactGeneratorTests.cs, test/WordSense.Builder.Tests/Data/InflectionRulesTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/form-generator.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/form-generator.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/form-generator.yaml */
    }
}
