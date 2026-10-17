package WordSense {
    part def WordPieceTokenizer {
        doc /* Splits text into BERT uncased word pieces with character offsets and builds model input windows. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense.MiniLM/WordPieceTokenizer.cs, src/DemaConsulting.WordSense.MiniLM/TokenizedText.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/WordPieceTokenizerTests.cs */
        comment designRef /* Design: docs/design/word-sense-mini-lm/word-piece-tokenizer.md */
        comment verificationRef /* Verification: docs/verification/word-sense-mini-lm/word-piece-tokenizer.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense-mini-lm/word-piece-tokenizer.yaml */
    }
}
