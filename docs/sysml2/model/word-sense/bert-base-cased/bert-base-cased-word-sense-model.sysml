package WordSense {
    part def BertBaseCasedWordSenseModel {
        doc /* The public bert-base-cased int8 model, which implements IDownloadableWordSenseModel: a core FallbackWordSenseModel of its internal encoder model (an OnnxWordSenseModel that pins the model identity, files and SHA-256 digests, the cased vocabulary, the hidden size 768 and the embedded weights and part-of-speech and tense head) and a Basic model, so that every ambiguous word the encoder does not score is answered by the Basic model, the slot probabilities of a word of several parts of speech are the head's, reweighed against those of the Basic model (head weight 0.8), and the tense of a same-spelling verb is the head's probability of past combined with the tense split of the Basic model (tense weight 0.9). */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.BertBaseCased/BertBaseCasedWordSenseModel.cs, src/DemaConsulting.WordSense.BertBaseCased/BertBaseCasedEncoderModel.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.BertBaseCased.Tests/BertBaseCasedWordSenseModelTests.cs, test/DemaConsulting.WordSense.BertBaseCased.Tests/EmbeddedHeadTests.cs */
        comment designRef /* Design: docs/design/word-sense/bert-base-cased/bert-base-cased-word-sense-model.md */
        comment verificationRef /* Verification: docs/verification/word-sense/bert-base-cased/bert-base-cased-word-sense-model.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/bert-base-cased/bert-base-cased-word-sense-model.yaml */
    }
}
