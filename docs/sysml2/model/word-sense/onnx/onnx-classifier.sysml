package WordSense {
    part def OnnxClassifier {
        doc /* The classifier of the encoder models: runs the encoder only for words that need context, scores the fine senses of every analysis of each such word with the per-lemma weights, answers at the sense, group or low-confidence group level by the lemma's two thresholds, chooses the lemma of a multi-lemma form, and answers an untrained word with the Basic model at the part of speech level. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxClassifier.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-classifier.yaml */
    }
}
