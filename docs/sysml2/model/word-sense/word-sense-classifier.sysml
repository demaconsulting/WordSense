package WordSense {
    part def WordSenseClassifier {
        doc /* Combines tokenizer, encoder and table in one flow to answer the part of speech and pronunciation of words in text: a lemma with one sense is answered from the dictionary, a lemma with several senses and a trained classifier is answered from context, and the encoder runs only when a word needs it. */

        comment sourceRef /* Source: src/DemaConsulting.WordSense/WordSenseClassifier.cs (also declares IWordSenseClassifier and WordSenseToken), src/DemaConsulting.WordSense/WordSenseClassifierOptions.cs, src/DemaConsulting.WordSense/FeatureExtractor.cs, src/DemaConsulting.WordSense/WordSenseModelMismatchException.cs */
        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordSenseClassifierTests.cs */
        comment designRef /* Design: docs/design/word-sense/word-sense-classifier.md */
        comment verificationRef /* Verification: docs/verification/word-sense/word-sense-classifier.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/word-sense-classifier.yaml */
    }
}
