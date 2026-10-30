package WordSense {
    part def OnnxSubsystem {
        doc /* The Onnx package DemaConsulting.WordSense.Onnx (depends only on core): the machinery shared by the encoder models, that is the abstract model (usable on its own) with its download and verification of model files, the ONNX encoder and tokenizer, the per-lemma weights and thresholds, the part-of-speech head (embedded, bound to its encoder and checked at load; a model that uses its head gives every word of several parts of speech the probabilities of its parts of speech from it), and the classifier that scores fine senses and chooses the answer level. Words whose senses it does not score are answered from the lexicon (with the probabilities of the head for a word of several parts of speech when the model uses its head), leaving the choice of their senses to the fallback model of a composition. The only package with a runtime dependency (ONNX Runtime). */

        comment testRef /* Test: test/DemaConsulting.WordSense.Onnx.Tests/OnnxWordSenseModelTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxClassifierPosTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/OnnxEncoderTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/WordPieceTokenizerTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/ModelDownloaderTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/DownloadMirrorTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/LemmaWeightsTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/PosHeadBlobTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/PosHeadTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/PossessiveViewTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/InFlightTrackerTests.cs, test/DemaConsulting.WordSense.Onnx.Tests/XmlDocExampleTests.cs */
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
        part posHead : PosHead;
    }
}
