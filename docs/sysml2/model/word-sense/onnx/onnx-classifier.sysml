package WordSense {
    part def OnnxClassifier {
        doc /* The classifier of the encoder models: runs the encoder only for words that need context and scores each such word's candidate senses with the per-lemma weights. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxClassifier.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-classifier.yaml */
    }
}
