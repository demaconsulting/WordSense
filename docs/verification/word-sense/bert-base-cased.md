## BertBaseCased Subsystem Verification Design

This document describes the verification of the BertBaseCased subsystem, the package
`DemaConsulting.WordSense.BertBaseCased`: the `bert-base-cased-int8` encoder (cased, hidden size 768) with the weights
trained for it embedded in the assembly, composed with the Basic model of the core package.

### Verification Approach

The subsystem is a thin layer on the Onnx package and the core Basic model, so verification concentrates on what the
layer adds: the
pinned model identity with four files, the cased tokenizer setting, the embedded weights and the composition with the
Basic model. Tests in
`test/DemaConsulting.WordSense.BertBaseCased.Tests` run the real int8 model (Git LFS) from `model/bert-base-cased-int8/`
against the embedded lexicon and weights through the public API (`BertBaseCasedWordSenseModelTests` and the end-to-end
`EndToEndTests`) and compile the XML
documentation examples (`XmlDocExampleTests`). Download behavior and tokenizer parity are verified once for all models in
the Onnx subsystem.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. The files `model_int8.onnx`, `vocab.txt`, `config.json` and
`tokenizer.json` under `model/bert-base-cased-int8/` (Git LFS) are needed; tests that need them are skipped when the LFS
content is absent (failed when `WORDSENSE_REQUIRE_MODELS` is set, as in CI). No network access. The ONNX Runtime
session of the tests is limited to two intra-op threads.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`BertBaseCasedWordSenseModel` unit pass.

### Test Scenarios

#### Real Model Answers Heteronyms in Context

A model created from the committed directory classifies the words of "They will record the record." with the verb
analysis for the first `record` and the noun analysis for the second, each answered at the part-of-speech level (the
lemma `record` is not trained, so the Basic model answers). A trained lemma (`back` in "I will go back to the house.")
is answered by the encoder at a sense or group level that the result states, with the senses of every analysis listed
whatever the level (`BertBaseCasedWordSenseModelTests`, `EndToEndTests`;
`BertBaseCasedWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`).

#### Reviewed Suite Does Not Regress

The model answers the 348 sentences of the reviewed suite (`corpus/suite`) at least as well as the expectation file
records for it: no row falls below its expectation (`fine`, `answer` or `miss`), so a regression fails the build while
the known misses stay documented. The test skips when the model files are not present
(`BertBaseCasedSuiteTests`, `BertBaseCasedSuite_ExpectedPasses_StillPass`).

#### Untrained Ambiguous Words Are Answered by the Basic Model

A word with several senses that the encoder does not score is answered with the complete result of the Basic model of
the composition (its analyses with their probabilities, the selected analysis, the level part of speech, or sense when
the selected analysis has exactly one sense, and no group id), and disposing the model disposes the encoder model and
the Basic model
(`BertBaseCasedWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`,
`BertBaseCasedWordSenseModel_Dispose_DisposesEncoderAndFallback`). The assembly references the core and Onnx
packages (`BertBaseCasedWordSenseModel_Assembly_ReferencesCoreAndOnnx`).

#### Embedded Weights Match the Word Image

The weights embedded for `bert-base-cased-int8` load for the model id and hidden size 768 and match the embedded word
image: the inventory hash is the one of the shipped image and every sense of every lemma resolves, with nothing
dropped (`EmbeddedWeights_BertBaseCased_MatchesWordImage`).

#### Case Is Part of the Context

The model keeps the case of the text: its model description is cased (the lower-casing setting is off,
`BertBaseCasedWordSenseModel_Identity_IsCased`), unlike that of MiniLM (`MiniLMWordSenseModel_Identity_IsPinned`). How
the cased and the uncased tokenizer settings tokenize is verified by the explicit cased and uncased scenarios of the
`WordPieceTokenizer` unit in the Onnx subsystem.

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is compiled against the real public API
(`XmlDocExampleTests`).

#### Package Notice Is Packed

The project file packs `NOTICE.md` at the root of the package, like the core package. The notice is reviewed with the
package (no automated test inspects the packed file): it reproduces the WordNet 3.0 license for the sense usage prior of
the weights, gives the Open English WordNet (CC BY 4.0) attribution for the sense ids they hold and records that the
encoder model files are downloaded from this repository under the Apache License 2.0.
