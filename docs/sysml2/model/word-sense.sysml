package WordSense {
    doc
    /*
     * WordSense is a .NET library that identifies the sense of English words in text: the lemma, the part of
     * speech and, where the model can tell, the Open English WordNet fine sense (a readable id linked to one OEWN
     * sense key) or the coarse group of senses, with the level at which the word was answered. It ships as five
     * layered NuGet packages. The core package DemaConsulting.WordSense holds the embedded word lexicon, the sense
     * catalog and the contracts (model, mirror options, answer values) and has no third-party dependency. Model
     * packages build on it: Basic chooses the analysis of a word with a statistical tagger and context cues; Onnx
     * holds the shared encoder machinery, the download of the encoder models and the fine-sense classifier, and
     * builds on Basic for the words it was not trained for; MiniLM and BertBaseCased are the two encoder models,
     * each one public class with its embedded weights and thresholds.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole: a lexicon and sense catalog that answer unambiguous words, and interchangeable word-sense models that resolve the ambiguous ones in context at the finest level they are confident in. */

        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSampleTests.cs, test/DemaConsulting.WordSense.Basic.Tests/BasicReadmeSampleTests.cs */
        comment designRef /* Design: docs/design/word-sense.md */
        comment verificationRef /* Verification: docs/verification/word-sense.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense.yaml */

        part core : CoreSubsystem;
        part basic : BasicSubsystem;
        part onnx : OnnxSubsystem;
        part miniLM : MiniLMSubsystem;
        part bertBaseCased : BertBaseCasedSubsystem;
        part wordSenseBuilder : WordSenseBuilder;
        part wordData : WordDataSubsystem;

        // Layer rule: Basic references only core; Onnx references core and Basic (the fallback for words that
        // the encoder weights do not cover); MiniLM and BertBaseCased reference Onnx.
        dependency onnxUsesBasic from onnx to basic;
    }
}
