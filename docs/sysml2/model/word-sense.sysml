package WordSense {
    doc
    /*
     * WordSense is a .NET library that tells the part of speech, and for words pronounced differently by
     * meaning the pronunciation, of English words in text. It ships as five layered NuGet packages. The core
     * package DemaConsulting.WordSense holds the embedded word lexicon and the contracts (model, mirror options,
     * answer values) and has no third-party dependency. Model packages build on it: Basic chooses senses with
     * a statistical tagger and context cues; Onnx holds the shared encoder machinery and download of the encoder models; MiniLM
     * and BertBaseCased are the two encoder models, each one public class with its embedded weights.
     */

    part def WordSenseSystem {
        doc /* The WordSense system as a whole: a lexicon that answers unambiguous words, and interchangeable word-sense models that resolve the ambiguous ones in context. */

        comment testRef /* Test: test/DemaConsulting.WordSense.MiniLM.Tests/ReadmeSampleTests.cs */
        comment designRef /* Design: docs/design/word-sense.md */
        comment verificationRef /* Verification: docs/verification/word-sense.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense.yaml */

        part core : CoreSubsystem;
        part basic : BasicSubsystem;
        part onnx : OnnxSubsystem;
        part miniLM : MiniLMSubsystem;
        part bertBaseCased : BertBaseCasedSubsystem;
        part wordSenseBuilder : WordSenseBuilder;
    }
}
