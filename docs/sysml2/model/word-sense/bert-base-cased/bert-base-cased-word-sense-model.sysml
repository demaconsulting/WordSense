package WordSense {
    part def BertBaseCasedWordSenseModel {
        doc /* The public bert-base-cased int8 model: a core FallbackWordSenseModel of its internal encoder model (an OnnxWordSenseModel that pins the model identity, files and SHA-256 digests, the cased vocabulary, the hidden size 768 and the embedded weights) and a Basic model, so that every ambiguous word the encoder does not score is answered by the Basic model. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.BertBaseCased/BertBaseCasedWordSenseModel.cs, src/DemaConsulting.WordSense.BertBaseCased/BertBaseCasedEncoderModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs */
        comment designRef /* Design: docs/design/word-sense/bert-base-cased/bert-base-cased-word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/bert-base-cased/bert-base-cased-word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/bert-base-cased/bert-base-cased-word-sense-model.yaml */
    }
}
