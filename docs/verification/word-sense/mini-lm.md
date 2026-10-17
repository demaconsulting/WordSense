## MiniLM Subsystem Verification Design

This document describes the verification of the MiniLM subsystem, the package `DemaConsulting.WordSense.MiniLM`: the
`all-MiniLM-L6-v2-int8` encoder (uncased, hidden size 384) with the weights trained for it embedded in the assembly.

### Verification Approach

The subsystem is a thin layer on the Onnx package, so verification concentrates on what the layer adds: the pinned model
identity and the embedded weights. Tests in `test/DemaConsulting.WordSense.MiniLM.Tests` run the real int8 model (Git
LFS) from `model/all-MiniLM-L6-v2-int8/` against the embedded lexicon and weights through the public API
(`MiniLMWordSenseModelTests`), compile and run the README sample (`ReadmeSampleTests`) and
compile the XML documentation examples (`XmlDocExampleTests`). Download behavior is not repeated here; it is verified
once for all models in the Onnx subsystem.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. The files `model_int8.onnx` and `vocab.txt` under
`model/all-MiniLM-L6-v2-int8/` (Git LFS) are needed; tests that need them are skipped when the LFS content is absent.
No network access. The ONNX Runtime session of the tests is limited to two intra-op threads.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`MiniLMWordSenseModel` unit pass.

### Test Scenarios

#### Real Model Classifies Heteronyms in Context

A model created from the committed directory classifies "The record was broken last year, so they will record the new
album." with a noun for the first and a verb for the second `record` (`MiniLMWordSenseModelTests`).

#### Embedded Weights Match the Lexicon and Drop Nothing

The weights embedded for `all-MiniLM-L6-v2-int8` load for the model id and hidden size 384, match the embedded lexicon
and drop zero lemmas.

#### README Sample Matches and Runs

The code block marked `<!-- sample:readme -->` in `README.md` equals the compiled README sample after normalizing line
endings and trailing white space, the compiled sample runs against the committed model, and the lines it writes equal
the block marked `<!-- output:readme -->`. The set of markers in `README.md` must equal the set of checked samples
(`ReadmeSampleTests`).

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is compiled against the real public API
(`XmlDocExampleTests`).
