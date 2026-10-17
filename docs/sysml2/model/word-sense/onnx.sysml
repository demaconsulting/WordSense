package WordSense {
    part def OnnxSubsystem {
        doc /* The Onnx package DemaConsulting.WordSense.Onnx: the machinery shared by the encoder models, that is the abstract model with its download and verification of model files, the ONNX encoder and tokenizer, the per-lemma weights and the classifier that scores senses. The only package with a runtime dependency (ONNX Runtime). */

        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/WordPieceTokenizerTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/ModelDownloaderTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/DownloadMirrorTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/LemmaWeightsTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/InFlightTrackerTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/onnx.md */
        comment verificationRef /* Verification: docs/verification/word-sense/onnx.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/onnx.yaml */

        part onnxWordSenseModel : OnnxWordSenseModel;
        part onnxClassifier : OnnxClassifier;
        part onnxEncoder : OnnxEncoder;
        part wordPieceTokenizer : WordPieceTokenizer;
        part modelDownloader : ModelDownloader;
        part downloadMirror : DownloadMirror;
        part lemmaWeights : LemmaWeights;
    }
}
