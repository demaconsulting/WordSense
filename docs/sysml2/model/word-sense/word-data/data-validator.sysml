package WordSense {
    part def DataValidator {
        doc /* Validator of the committed tables against the schema: format, keys, foreign keys, sort order, placement, relation conventions, cross-table rules, and the rules of the sense inventory (labels, groups and their partition, project senses, the legacy class map). */

        comment sourceRef /* Source: tools/WordSense.Builder/Data/DataValidator.cs */
        comment testRef /* Test: test/WordSense.Builder.Tests/Data/DataValidatorTests.cs, test/WordSense.Builder.Tests/Data/DataEndToEndTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-data/data-validator.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-data/data-validator.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-data/data-validator.yaml */
    }
}
