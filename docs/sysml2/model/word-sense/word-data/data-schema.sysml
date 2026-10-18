package WordSense {
    part def DataSchema {
        doc /* Loader of data/schema.yaml (tables, columns, keys, vocabularies, relation types, partition rules) with its YAML-subset reader and the rewriter of the file split line. */

        comment sourceRef /* Source: tools/WordSense.Builder/Data/DataSchema.cs, tools/WordSense.Builder/Data/SchemaYaml.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/Data/DataSchemaTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/data-schema.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/data-schema.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/data-schema.yaml */
    }
}
