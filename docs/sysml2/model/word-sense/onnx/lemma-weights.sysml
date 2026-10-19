package WordSense {
    part def LemmaWeights {
        doc /* The trained per-lemma classifiers of an encoder model: reads the embedded weights blob (fine-sense rows, the two thresholds and the optional form chooser of each lemma), matches it per sense id to the word lexicon (an inventory hash or sense id that does not belong to the lexicon is a load failure), and computes the feature, the fine scores, the group scores and the per-level confidence of a word. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/LemmaWeights.cs, src/DemaConsulting.WordSense.Onnx/WeightsBlob.cs, src/DemaConsulting.WordSense.Onnx/WeightedLemmaTable.cs, src/DemaConsulting.WordSense.Onnx/FeatureExtractor.cs, src/DemaConsulting.WordSense.Onnx/VectorMath.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/LemmaWeightsTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/WeightsBlobTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/WeightedLemmaTableTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/EmbeddedWeightsTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/lemma-weights.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/lemma-weights.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/lemma-weights.yaml */
    }
}
