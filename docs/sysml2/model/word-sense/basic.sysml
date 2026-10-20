package WordSense {
    part def BasicSubsystem {
        doc /* The Basic package DemaConsulting.WordSense.Basic (depends only on core): a word-sense model that needs no model files, weights or network, and chooses the analysis of an ambiguous word with a part-of-speech tagger and context cues over embedded statistics, listing every analysis with its probability and answering at the part of speech level (at the sense level where the selected analysis has one sense). */

        comment testRef /* Test: test/DemaConsulting.WordSense.Basic.Tests/BasicWordSenseModelTests.cs, test/DemaConsulting.WordSense.Basic.Tests/BasicClassifierTests.cs, test/DemaConsulting.WordSense.Basic.Tests/BasicTablesTests.cs, test/DemaConsulting.WordSense.Basic.Tests/PosTaggerTests.cs, test/DemaConsulting.WordSense.Basic.Tests/CueModelTests.cs, test/DemaConsulting.WordSense.Basic.Tests/SenseFusionTests.cs, test/DemaConsulting.WordSense.Basic.Tests/BasicReadmeSampleTests.cs, test/DemaConsulting.WordSense.Basic.Tests/ModelTableReadmeTests.cs, test/DemaConsulting.WordSense.Basic.Tests/XmlDocExampleTests.cs */
        comment designRef /* Design: docs/design/word-sense/basic.md */
        comment verificationRef /* Verification: docs/verification/word-sense/basic.md */
        comment reqRef /* Requirements: docs/reqstream/word-sense/basic.yaml */

        part basicWordSenseModel : BasicWordSenseModel;
        part basicClassifier : BasicClassifier;
        part basicTables : BasicTables;
        part posTagger : PosTagger;
        part cueModel : CueModel;
        part senseFusion : SenseFusion;
    }
}
