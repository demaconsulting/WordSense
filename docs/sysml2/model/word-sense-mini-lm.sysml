package WordSense {
    part def WordSenseMiniLMSystem {
        doc /* The MiniLM package as a whole: the ONNX MiniLM encoder, its tokenizer, the download of its model files, and the weights table trained for it, with a factory that wires them to the core classifier. */

        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/WordSenseTests.cs */
        comment designRef /* Design: docs/design/word-sense-mini-lm.md */
        comment verificationRef /* Verification: docs/verification/word-sense-mini-lm.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense-mini-lm.yaml */

        part miniLMWordSense : MiniLMWordSense;
        part miniLMWordSenseModel : MiniLMWordSenseModel;
        part wordPieceTokenizer : WordPieceTokenizer;
        part wordSenseModelDownloader : WordSenseModelDownloader;
        part downloadMirror : DownloadMirror;
    }
}
