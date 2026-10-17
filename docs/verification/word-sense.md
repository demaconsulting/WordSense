# System Verification Design

This document describes the system-level verification strategy for WordSense, the set of five layered NuGet packages
`DemaConsulting.WordSense` (core), `DemaConsulting.WordSense.Basic`, `DemaConsulting.WordSense.Onnx`,
`DemaConsulting.WordSense.MiniLM` and `DemaConsulting.WordSense.BertBaseCased`, together with the non-shipped builder
tool that generates their embedded data.

## Verification Approach

The system is verified in layers that follow the package layers. Each package has its own test project, and each
test project verifies the subsystem of that package through its public API and its units through the internal API
(made visible to the test project by `InternalsVisibleTo`):

- `test/DemaConsulting.WordSense.Tests` verifies the core package with the embedded word lexicon only. It needs no
  model, no model file and no network access.
- `test/DemaConsulting.WordSense.Basic.Tests` verifies the statistical model against the embedded word lexicon, toy
  statistical tables and the embedded table.
- `test/DemaConsulting.WordSense.Onnx.Tests` verifies the shared encoder implementation with small substitutes:
  hand-built vocabularies, fake weights and a loopback `HttpListener` for downloads. Its model-level tests
  (`OnnxWordSenseModelTests`, `OnnxClassifierTests`, `OnnxEncoderTests`) use test doubles or the committed int8 model.
- `test/DemaConsulting.WordSense.MiniLM.Tests` and `test/DemaConsulting.WordSense.BertBaseCased.Tests` verify each
  model package against the real int8 model and the weights embedded in the package. The MiniLM tests also compile the
  README sample against the public API and run it against the Basic, MiniLM and BertBaseCased models.
- `test/WordSense.Builder.Tests` verifies the builder commands with synthetic data in temporary directories.

The system-level evidence is the combination of these projects: the end-to-end classification scenarios of the model
packages, the dependency structure of the packages (verified by inspecting the project references) and the platform
runs of the CI matrix. Unit-level behavior is verified in the unit documents and subsystem-level behavior in the
subsystem documents, with substitutes where a real model is not needed.

## Test Environment

- **Framework**: xUnit v3 running under the .NET SDK on net8.0, net9.0 and net10.0
- **Execution**: `dotnet test` invoked by `build.ps1` and the CI pipeline (Git LFS content is fetched)
- **Platforms**: Windows, Linux and macOS in the CI matrix
- **Dependencies**: the core, Basic and tokenizer tests have none. The real-model tests need the Git LFS files under
  `model/all-MiniLM-L6-v2-int8/` (`model_int8.onnx`, `vocab.txt`) and `model/bert-base-cased-int8/`
  (`model_int8.onnx`, `vocab.txt`, `config.json`, `tokenizer.json`); no network access. Tests that need a model are
  skipped, not failed, when the LFS content is absent.
- **Isolation**: each test creates its own model, classifier and store directory; no shared state.

## Acceptance Criteria

The system passes verification when every test of every test project passes on every platform and target framework of
the CI matrix, the real-model end-to-end scenarios below give the documented parts of speech, and the project
references show the layering. Requirements-to-test coverage is tracked by the ReqStream trace matrix.

## Test Scenarios

### End-to-End Classification with a Real Model

The MiniLM and BertBaseCased test projects each create the model from the committed model directory, call
`EnsureModelAsync` and `ClassifyAsync`, and classify the words of "They will record the record." The first `record`
must be a verb and the second a noun. This scenario is run on each CI operating system
(Windows, Linux, macOS) and each target framework (net8.0, net9.0, net10.0); the platform requirements are each linked
to it with a source filter (`windows@`, `ubuntu@`, `macos@`, `net8.0@`, `net9.0@`, `net10.0@`), so a requirement is
satisfied only by a passing result from its own platform or framework.

### Lexicon-Only Classification with the Basic Model

The Basic test project classifies a short word list through `BasicWordSenseModel`, with no model files: a word with
one sense is answered with confidence 1, a word without a lexicon entry is unknown with confidence 0, and a word with
several senses is answered with the probability of its chosen sense. Repeated calls give identical results, and the
embedded statistics table loads on first use.

### Core Package Has No ONNX Dependency

The core package must not reference ONNX Runtime or any model package. This is verified by inspecting the project
references of the core project: its only references are the framework and build-time analyzers. The Basic package
references only the core package; the Onnx package references the core package and ONNX Runtime; the MiniLM and
BertBaseCased packages reference only the Onnx package (and, transitively, the core package).

### Packages Share One Contract

A model of each package (Basic, MiniLM, BertBaseCased) is used through `IWordSenseModel`
only. For a word with a single sense every package gives the same answer, the lexicon entry with confidence 1 (this
includes the determiners such as `the`, in any case); for an unknown word every package gives `(word, null, 0)`.

### Embedded Data Is Consistent

The embedded word lexicon of the core package loads, and the weights embedded in each model package load, match the
lexicon and drop no lemma (see the MiniLM and BertBaseCased subsystem documents). A weights blob for another model id
or dimension is refused when loaded.

### Documentation Samples Compile and Run

The README sample is extracted by marker from `README.md` and compared with the compiled copies in the test projects
after normalizing line
endings and trailing white space. The set of markers must equal the set of checked samples. The sample runs against
each model (a stand-in model class wraps Basic, or the committed MiniLM or BertBaseCased model) and gives the output
documented for it; the two commented alternative lines are checked as text and their model classes are created.
Tests that need model files are skipped when the files are absent. The user guide samples are reviewed with the guide
and are not compiled. The XML documentation examples of every package are
extracted from the shipped XML files and compiled against the public API (the `XmlDocExampleTests` of each test
project).

### Builder Commands

The builder verification document describes the commands `words`, `import-moby`, `weights`, `evaluate`, `ud-counts`,
`train-basic`, `tune-basic` and
`appendix`. Together they regenerate the data that is committed in the packages, and the committed appendix of the user
guide is compared with a regenerated one.
