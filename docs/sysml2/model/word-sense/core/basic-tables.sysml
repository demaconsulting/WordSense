package WordSense {
    part def BasicTables {
        doc /* The embedded statistics of the basic model: reads and validates the compressed table (format WSB7) of transition counts, clause-state counts (the positions whose clause already has a verb) with the smoothed transition probabilities of both clause states, tabled words with closed-class masks, suffix statistics, cue tables keyed by training lemma whose columns are analyses, each with a reliability factor that damps a table that predicts its own held-out sentences poorly, the four numbers of the word prior of the tagger, and the shared tense table (a prior and a hashed weight per context feature, trained on all verbs) with the weight of the tense model, and the shared noun/verb table (a hashed weight per context feature, fitted on the words of the training split of the open corpus that have a noun and a verb analysis) with the weight of the noun/verb model, bound to the word image by its inventory hash. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/BasicTables.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/BasicTablesTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/basic-tables.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/basic-tables.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/basic-tables.yaml */
    }
}
