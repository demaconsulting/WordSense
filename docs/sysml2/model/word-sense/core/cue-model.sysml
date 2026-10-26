package WordSense {
    part def CueModel {
        doc /* The context-cue model of the basic model: extracts context features around a target word and scores the candidate analyses of a form with hashed naive-Bayes weights; its tense features (the cue features, the window told apart by side, the segment edge and the nearest function word that carries tense) score a verb as past or non-past from the shared tense table, and its noun/verb features (the word and the class of the two tokens on each side) give the log odds of verb against noun from the shared noun/verb table. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/CueModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/CueModelTests.cs */
        comment designRef /* Design: docs/design/word-sense/core/cue-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core/cue-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core/cue-model.yaml */
    }
}
