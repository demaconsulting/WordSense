package WordSense {
    part def MiniLMWordSenseModel {
        doc /* The public all-MiniLM-L6-v2 int8 model: a core FallbackWordSenseModel of its internal encoder model (an OnnxWordSenseModel that pins the model identity, files and SHA-256 digests, the uncased vocabulary, the hidden size 384 and the embedded weights) and a Basic model, so that every ambiguous word the encoder does not score is answered by the Basic model. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.MiniLM/MiniLMWordSenseModel.cs, src/DemaConsulting.WordSense.MiniLM/MiniLMEncoderModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/MiniLMWordSenseModelTests.cs */
        comment designRef /* Design: docs/design/word-sense/mini-lm/mini-lm-word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/mini-lm/mini-lm-word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/mini-lm/mini-lm-word-sense-model.yaml */
    }
}
