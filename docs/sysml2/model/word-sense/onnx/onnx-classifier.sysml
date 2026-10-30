package WordSense {
    part def OnnxClassifier {
        doc /* The classifier of the encoder models: runs the encoder only for words that need context (a word with scored senses or, with a part-of-speech head, a known word of several parts of speech), scores the fine senses of every analysis of each such word with the per-lemma weights, gives each analysis a probability (per lemma and part of speech, the feature variants of one sharing its probability; with a head, the head's probability of the part of speech times the share of the slot among the slots of that part of speech) and lists the ranked senses inside it, answers at the sense level, the group level (for a lemma that has groups) or else the part of speech by the lemma's thresholds, with the confidence built on the probability of the lemma and part of speech, chooses the analysis of a multi-lemma form, and answers a word without scored senses from the lexicon (its lemmas and parts of speech equally likely, or with the head's probabilities, no scored senses, the part of speech level) for a fallback model to decide. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxClassifier.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierPosTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/Fixtures.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-classifier.yaml */
    }
}
