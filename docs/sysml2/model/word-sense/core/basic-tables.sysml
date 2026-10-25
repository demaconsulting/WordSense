package WordSense {
    part def BasicTables {
        doc /* The embedded statistics of the basic model: reads and validates the compressed table (format WSB4) of transition counts, tabled words with closed-class masks, suffix statistics, cue tables keyed by training lemma whose columns are analyses, and the shared tense table (a prior and a hashed weight per context feature, trained on all verbs) with the weight of the tense model, bound to the word image by its inventory hash. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/BasicTables.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/BasicTablesTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/basic-tables.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/basic-tables.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/basic-tables.yaml */
    }
}
