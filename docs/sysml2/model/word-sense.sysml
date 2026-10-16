package WordSense {
    doc
    /*
     * WordSense is a .NET library that tells which sense an ambiguous English word carries in
     * context (for example "record" as noun or verb) using a quantized MiniLM encoder and a
     * compact embedded classifier table. It ships as the single package DemaConsulting.WordSense.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseTests.cs, test/DemaConsulting.WordSense.Tests/ReadmeSampleTests.cs */
        comment designRef /* Design: docs/design/word-sense.md */
        comment verificationRef /* Verification: docs/verification/word-sense.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense.yaml */

        part wordPieceTokenizer : WordPieceTokenizer;
        part wordSenseTable : WordSenseTable;
        part senseRecord : SenseRecord;
        part onnxWordSenseEncoder : OnnxWordSenseEncoder;
        part wordSenseClassifier : WordSenseClassifier;
        part downloadMirror : DownloadMirror;
        part wordSenseModelDownloader : WordSenseModelDownloader;
        part wordSenseBuilder : WordSenseBuilder;
    }
}
