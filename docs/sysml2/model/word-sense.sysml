package WordSense {
    doc
    /*
     * WordSense is a .NET library that tells the part of speech, and for words pronounced differently by
     * meaning the pronunciation, of English words in text. Words with one part of speech are answered from
     * an embedded dictionary; ambiguous words (for example "record" as noun or verb) are answered in context
     * using an encoder model and compact per-lemma classifiers. The core package DemaConsulting.WordSense
     * holds the lexicon, the answer values and the model abstraction and has no ONNX dependency; the
     * package DemaConsulting.WordSense.MiniLM supplies the ONNX MiniLM encoder.
     */

    part def WordSenseSystem {
        doc /* The core WordSense system as a whole; it answers from the lexicon alone and uses an encoder model only when one is supplied. */

        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/WordSenseTests.cs, test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSampleTests.cs, test/DemaConsulting.WordSense.MiniLM.Tests/UserGuideSampleTests.cs */
        comment designRef /* Design: docs/design/word-sense.md */
        comment verificationRef /* Verification: docs/verification/word-sense.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense.yaml */

        part wordSenseTable : WordSenseTable;
        part senseRecord : SenseRecord;
        part wordSenseModel : WordSenseModel;
        part wordSenseClassifier : WordSenseClassifier;
        part wordSenseBuilder : WordSenseBuilder;
    }
}
