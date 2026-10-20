package WordSense {
    part def BasicTables {
        doc /* The embedded statistics of the basic model: reads and validates the compressed table of transition counts, tabled words with closed-class masks, suffix statistics and cue tables keyed by training lemma whose columns are analyses, bound to the word image by its inventory hash. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Basic/BasicTables.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Basic.Tests/BasicTablesTests.cs */
        comment designRef /* Design: docs/design/word-sense/basic/basic-tables.md */
        comment verificationRef /* Verification: docs/verification/word-sense/basic/basic-tables.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/basic/basic-tables.yaml */
    }
}
