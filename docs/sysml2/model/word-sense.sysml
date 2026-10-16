package WordSense {
    doc
    /*
     * WordSense is a .NET library that tells the part of speech, and for words pronounced differently by
     * meaning the pronunciation, of English words in text. Words with one part of speech are answered from
     * an embedded dictionary; ambiguous words (for example "record" as noun or verb) are answered in context
     * using a quantized MiniLM encoder and compact per-lemma classifiers held in the same embedded table.
     * It ships as the single package DemaConsulting.WordSense.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseTests.cs, test/DemaConsulting.WordSense.Tests/ReadmeSampleTests.cs, test/DemaConsulting.WordSense.Tests/UserGuideSampleTests.cs */
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
