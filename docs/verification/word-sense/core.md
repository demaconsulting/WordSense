## Core Subsystem Verification Design

This document describes the verification of the core subsystem, the package `DemaConsulting.WordSense` in the
namespace `DemaConsulting.WordSense`: the embedded word lexicon and the contracts that every model package
implements.

### Verification Approach

The subsystem is verified through its public API with the embedded lexicon. No model, fake or file is needed: the
contracts are plain types and interfaces, and the lexicon is read from the assembly resource. Tests in
`test/DemaConsulting.WordSense.Tests` (`WordLexiconTests`, `ContractsTests`, `WordsBlobTests`) verify the units
`WordLexicon` and `WordSenseContracts`, and `XmlDocExampleTests` compiles the XML documentation examples of the package
against the public API. The subsystem boundary to the model packages is verified by the model packages themselves,
which implement the contracts.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. No model files, no network access and no environment variables
are needed.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`WordLexicon` and `WordSenseContracts` units pass.

### Test Scenarios

#### Lexicon Loads and Serves Known Words

The embedded lexicon holds entries for common lemmas and their inflected forms; a heteronym such as `read` has two
senses and a word with one sense has one. The scenarios are detailed in the `WordLexicon` unit document.

#### Contracts Are Usable Without a Model Package

`WordSense`, `WordDetails`, `PartOfSpeech`, `MirrorOptions`, `IWordSenseModel` and `IWordSenseClassifier` can be used
by a test double with no model package referenced (`ContractsTests`). The scenarios are detailed in the
`WordSenseContracts` unit document.

#### No ONNX Dependency

The package does not reference ONNX Runtime or any other WordSense package; the automated test
`WordSenseContracts_CoreAssembly_HasNoOnnxOrWeightsReferences` (`ContractsTests`) checks the assembly references, and it
is why the core test project runs without model files.

#### XML Documentation Examples Compile

Every `<code>` example in the shipped XML documentation file (`DemaConsulting.WordSense.xml`) is extracted and compiled
against the real public API, so a documented example cannot drift from the API (`XmlDocExampleTests`).
