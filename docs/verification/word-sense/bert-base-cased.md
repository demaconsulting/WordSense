## BertBaseCased Subsystem Verification Design

This document describes the verification of the BertBaseCased subsystem, the package
`DemaConsulting.WordSense.BertBaseCased`: the `bert-base-cased-int8` encoder (cased, hidden size 768) with the weights
trained for it embedded in the assembly.

### Verification Approach

The subsystem is a thin layer on the Onnx package, so verification concentrates on what the layer adds: the pinned model
identity with four files, the cased tokenizer setting and the embedded weights. Tests in
`test/DemaConsulting.WordSense.BertBaseCased.Tests` run the real int8 model (Git LFS) from `model/bert-base-cased-int8/`
against the embedded lexicon and weights through the public API (`BertBaseCasedWordSenseModelTests` and the end-to-end
`EndToEndTests`) and compile the XML
documentation examples (`XmlDocExampleTests`). Download behavior and tokenizer parity are verified once for all models in
the Onnx subsystem.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. The files `model_int8.onnx`, `vocab.txt`, `config.json` and
`tokenizer.json` under `model/bert-base-cased-int8/` (Git LFS) are needed; tests that need them are skipped when the LFS
content is absent. No network access. The ONNX Runtime session of the tests is limited to two intra-op threads.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`BertBaseCasedWordSenseModel` unit pass.

### Test Scenarios

#### Real Model Classifies Heteronyms in Context

A model created from the committed directory classifies "The record was broken last year, so they will record the new
album." with a noun for the first and a verb for the second `record`.

#### Embedded Weights Match the Lexicon and Drop Nothing

The weights embedded for `bert-base-cased-int8` load for the model id and hidden size 768, match the embedded lexicon
and drop zero lemmas.

#### Case Is Part of the Context

The model keeps the case of the text; the cased tokenizer setting differs from that of MiniLM, which is verified by
the tokenizer parity scenarios of the Onnx subsystem.

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is compiled against the real public API
(`XmlDocExampleTests`).
