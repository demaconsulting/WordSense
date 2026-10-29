package WordSense {
    part def PosHead {
        doc /* The trained part-of-speech head of an encoder model: reads the embedded head blob (a linear classifier over the pooled vectors of a word and its previous and next word, with the digest of the encoder file and the classes it was trained for), binds it at load to the pinned encoder file, the hidden size, the model identifier and the parts of speech of the library (a mismatch is a load failure), computes the probability of every part of speech of a word with fixed-order scalar arithmetic and restricts the probabilities to an allowed set of parts of speech. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.Onnx/PosHead.cs, src/DemaConsulting.WordSense.Onnx/PosHeadBlob.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/PosHeadBlobTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/PosHeadTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/EmbeddedHeadTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/HeadBlobWriter.cs */
        comment designRef /* Design: docs/design/word-sense/onnx/pos-head.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx/pos-head.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx/pos-head.yaml */
    }
}
