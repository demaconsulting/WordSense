package WordSense {
    doc
    /*
     * WordSense is a .NET library that identifies the sense of English words in text: the lemma, the part of
     * speech and, where the model can tell, the Open English WordNet fine sense (a readable id linked to one OEWN
     * sense key) or the group of closely related, easily confused senses where one was curated, with the level at
     * which the word was answered, the ranked analyses of the word and, where the model scored them, the ranked
     * senses of each analysis. It ships as five
     * layered NuGet packages. The core package DemaConsulting.WordSense holds the embedded word lexicon, the sense
     * catalog and the contracts (model, composite fallback model, mirror options, answer values) and has no
     * third-party dependency. Model
     * packages build on it: Basic chooses the analysis of a word with a statistical tagger and context cues; Onnx
     * holds the shared encoder machinery, the download of the encoder models and the fine-sense classifier, and
     * depends only on the core package; MiniLM and BertBaseCased are the two encoder models, each one public class
     * that composes its encoder model with the Basic model for the words the encoder weights do not cover, and
     * carries its embedded weights and thresholds.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole: a lexicon and sense catalog that answer unambiguous words, and interchangeable word-sense models that resolve the ambiguous ones in context at the finest level they are confident in, listing the ranked analyses and, where scored, the ranked senses of each. */

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

        // Layer rule: Basic and Onnx reference only core; MiniLM and BertBaseCased reference core, Onnx and Basic
        // (each composes its encoder model with the Basic model through the core FallbackWordSenseModel).
        dependency miniLMUsesOnnx from miniLM to onnx;
        dependency miniLMUsesBasic from miniLM to basic;
        dependency bertBaseCasedUsesOnnx from bertBaseCased to onnx;
        dependency bertBaseCasedUsesBasic from bertBaseCased to basic;
    }
}
