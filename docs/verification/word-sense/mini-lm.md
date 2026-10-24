## MiniLM Subsystem Verification Design

This document describes the verification of the MiniLM subsystem, the package `DemaConsulting.WordSense.MiniLM`: the
`all-MiniLM-L6-v2-int8` encoder (uncased, hidden size 384) with the weights trained for it embedded in the assembly,
composed with the Basic model of the core package.

### Verification Approach

The subsystem is a thin layer on the Onnx package and the core Basic model, so verification concentrates on what the
layer adds: the
pinned model identity, the embedded weights and the composition with the Basic model. Tests in
`test/DemaConsulting.WordSense.MiniLM.Tests` run the real int8 model (Git LFS) from `model/all-MiniLM-L6-v2-int8/`
against the embedded lexicon and weights through the public API
(`MiniLMWordSenseModelTests`), compile and run the README sample (`ReadmeSampleTests`) and
compile the XML documentation examples (`XmlDocExampleTests`). Download behavior is not repeated here; it is verified
once for all models in the Onnx subsystem.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. The files `model_int8.onnx` and `vocab.txt` under
`model/all-MiniLM-L6-v2-int8/` (Git LFS) are needed, and for the README sample also `model_int8.onnx` and `vocab.txt`
under `model/bert-base-cased-int8/`; tests that need them are skipped when the LFS content is absent (failed when
`WORDSENSE_REQUIRE_MODELS` is set, as in CI).
No network access. The ONNX Runtime session of the tests is limited to two intra-op threads.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`MiniLMWordSenseModel` unit pass.

### Test Scenarios

#### Real Model Answers Heteronyms in Context

A model created from the committed directory classifies the words of "They will record the record." with the verb
analysis for the first `record` and the noun analysis for the second, each answered at the part-of-speech level (the
lemma `record` is not trained, so the Basic model answers). A trained lemma (`back` in "I will go back to the house.")
is answered by the encoder at a sense or group level that the result states, with the senses of every analysis listed
whatever the level (`MiniLMWordSenseModelTests`, `EndToEndTests`;
`MiniLMWordSenseModel_Classify_TrainedLemma_AnswersAtASenseOrGroupLevel`). The end-to-end check answers six clear
sentences (the trained lemmas `back`, `right`, `home`, `help`, `test`, `contract` and `dawn`), requires that every
answer follows the level contract (a sense carries its id and group, a group only its id, a part of speech neither) and
that the answers reach the sense level at least once; the explicit witness is the sentence "The farmers get up at dawn
to milk the cows ." (tokens as in the test), in which `dawn` (a lemma that no other sentence uses) must be answered at
the sense level (`EndToEndTests`, `WordSense_Classifier_EndToEnd_AnswersAtTheFinestConfidentLevel`).

#### Reviewed Suite Does Not Regress

The model answers the 348 sentences of the reviewed suite (`corpus/suite`) at least as well as the expectation file
records for it: no row falls below its expectation (`fine`, `answer` or `miss`), so a regression fails the build while
the known misses stay documented. The test skips when the model files are not present
(`MiniLMSuiteTests`, `MiniLMSuite_ExpectedPasses_StillPass`).

#### Untrained Ambiguous Words Are Answered by the Basic Model

A word with several senses that the encoder does not score is answered with the complete result of the Basic model of
the composition (its analyses with their probabilities, the selected analysis, the level part of speech, or sense when
the selected analysis has exactly one sense, and no group id), and disposing the model disposes the encoder model and
the Basic model
(`MiniLMWordSenseModel_Classify_UntrainedAmbiguousWord_AnswersFromBasicFallback`,
`MiniLMWordSenseModel_Dispose_DisposesEncoderAndFallback`). The assembly references the core and Onnx packages
(`MiniLMWordSenseModel_Assembly_ReferencesCoreAndOnnx`).

#### Embedded Weights Match the Word Image

The weights embedded for `all-MiniLM-L6-v2-int8` load for the model id and hidden size 384 and match the embedded word
image: the inventory hash is the one of the shipped image and every sense of every lemma resolves, with nothing
dropped (`EmbeddedWeights_MiniLM_MatchesWordImage`).

#### Model Offers the Optional Preparation Step

The model is an `IDownloadableWordSenseModel` (`MiniLMWordSenseModel_IsDownloadable`), so a caller that holds only an
`IWordSenseModel` can ask for the capability with a type check; `EnsureModelAsync` forwards to the encoder model, and a
model directory that does not exist is reported as `ModelUnavailableException`
(`MiniLMWordSenseModel_EnsureModelAsync_MissingDirectory_ThrowsModelUnavailable`). The download behavior itself is
verified once for all models in the Onnx subsystem.

#### README Sample Matches and Runs

The code block marked `<!-- sample:readme -->` in `README.md` equals the compiled README sample after normalizing line
endings and trailing white space, and the compiled sample runs with the Basic model, with the committed MiniLM model
and with the committed BertBaseCased model; the lines it writes equal the blocks marked `<!-- output:basic -->`,
`<!-- output:minilm -->` and `<!-- output:bertbasecased -->`. The Quick Start sample does not call
`EnsureModelAsync`: `ClassifyAsync` prepares the model on first need. The two commented alternatives in the sample name
model classes that exist and can be created. Two further compile-only samples show the optional preparation step: the
block marked `<!-- sample:download -->` calls `await model.EnsureModelAsync();` (and the overload with
`DownloadOptions`) directly on the concrete MiniLM model, and the block marked `<!-- sample:capability -->` asks an
`IWordSenseModel` for the capability with `if (model is IDownloadableWordSenseModel downloadable)` and calls
`await downloadable.EnsureModelAsync();`; each equals its compiled sample
(`WordSense_Readme_DownloadBlock_MatchesCompiledSample`, `WordSense_Readme_CapabilityBlock_MatchesCompiledSample`).
The set of markers in `README.md` (`readme`, `download`, `capability`) must equal the set of checked samples
(`WordSense_Readme_SampleMarkers_AreAllChecked`, `ReadmeSampleTests`).

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is compiled against the real public API
(`XmlDocExampleTests`).

#### Package Notice Is Packed

The project file packs `NOTICE.md` at the root of the package, like the core package. The notice is reviewed with the
package (no automated test inspects the packed file): it reproduces the WordNet 3.0 license for the sense usage prior of
the weights, gives the Open English WordNet (CC BY 4.0) attribution for the sense ids they hold and records that the
encoder model files are downloaded from this repository under the Apache License 2.0.
