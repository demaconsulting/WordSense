package WordSense {
    part def CoreSubsystem {
        doc /* The core package DemaConsulting.WordSense: the embedded word lexicon, the sense catalog (senses, groups, OEWN keys), the contracts every model implements, the composite fallback model and the built-in statistical Basic model (a part-of-speech tagger and context cues over embedded statistics, answering at the part of speech level and at the sense level where the selected analysis has one sense, with no model files, weights or network). It has no ONNX Runtime, encoder weights or other third-party dependency. */

        comment testRef /* Test: test/DemaConsulting.WordSense.Tests/WordLexiconTests.cs, test/DemaConsulting.WordSense.Tests/ContractsTests.cs, test/DemaConsulting.WordSense.Tests/BasicWordSenseModelTests.cs, test/DemaConsulting.WordSense.Tests/BasicClassifierTests.cs, test/DemaConsulting.WordSense.Tests/BasicTablesTests.cs, test/DemaConsulting.WordSense.Tests/PosTaggerTests.cs, test/DemaConsulting.WordSense.Tests/CueModelTests.cs, test/DemaConsulting.WordSense.Tests/SenseFusionTests.cs, test/DemaConsulting.WordSense.Tests/ReferenceTokenizerTests.cs, test/DemaConsulting.WordSense.Tests/BasicReadmeSampleTests.cs, test/DemaConsulting.WordSense.Tests/ModelTableReadmeTests.cs, test/DemaConsulting.WordSense.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/core.md */
        comment verificationRef /* Verification: docs/verification/word-sense/core.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/core.yaml */

        part wordLexicon : WordLexicon;
        part wordSenseContracts : WordSenseContracts;
        part basicWordSenseModel : BasicWordSenseModel;
        part basicClassifier : BasicClassifier;
        part basicTables : BasicTables;
        part posTagger : PosTagger;
        part cueModel : CueModel;
        part senseFusion : SenseFusion;
    }
}
