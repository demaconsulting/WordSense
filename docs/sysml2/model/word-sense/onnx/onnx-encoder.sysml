package WordSense {
    part def OnnxEncoder {
        doc /* The BERT-style ONNX encoder: tokenizes the words, runs the inference session and returns one mean-pooled hidden-state vector per target word. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxEncoder.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-encoder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-encoder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-encoder.yaml */
    }
}
