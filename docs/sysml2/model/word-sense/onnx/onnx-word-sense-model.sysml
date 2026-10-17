package WordSense {
    part def OnnxWordSenseModel {
        doc /* The public abstract base of the encoder models: it makes the model files available and verified, opens the ONNX encoder and embedded weights on first use, and creates classifiers; disposing it releases the encoder and cancels a download in flight. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxWordSenseModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-word-sense-model.yaml */
    }
}
