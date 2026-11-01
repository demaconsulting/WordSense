package WordSense {
    part def OnnxEncoder {
        doc /* The BERT-style ONNX encoder: tokenizes the words, runs the inference session and returns one mean-pooled hidden-state vector per target word, or per word of a whole text (a text longer than one window is encoded in overlapping windows and each word is pooled from the window in which it has the most context), and offers the possessive view of a text in which a possessive written as one token is split into its stem and its marker. A model with sense layers returns the mean of the pooled layers (read from the tap outputs of the graph) as the sense vector and the last layer for the heads, and a graph without a tap is rejected. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/OnnxEncoder.cs, src/DemaConsulting.WordSense.Onnx/FeatureLayout.cs, src/DemaConsulting.WordSense.Onnx/PossessiveView.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderLayerTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/TappedGraph.cs, test/DemaConsulting.WordSense.Onnx.Tests/PossessiveViewTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/onnx-encoder.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/onnx-encoder.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/onnx-encoder.yaml */
    }
}
