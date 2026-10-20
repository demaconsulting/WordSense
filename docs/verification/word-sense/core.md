## Core Subsystem Verification Design

This document describes the verification of the core subsystem, the package `DemaConsulting.WordSense` in the
namespace `DemaConsulting.WordSense`: the embedded word lexicon, the sense catalog and the contracts that every model
package implements.

### Verification Approach

The subsystem is verified through its public API with the embedded lexicon and catalog. No model, fake or file is
needed: the contracts are plain types and interfaces, and the lexicon and catalog are read from the assembly resource.
Tests in `test/DemaConsulting.WordSense.Tests` (`WordLexiconTests`, `ContractsTests`) verify the units
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

The embedded lexicon holds entries for common lemmas and their inflected forms; a form that belongs to several lemmas
or tenses, such as `read`, lists one analysis for each, and a word with one analysis lists one. The scenarios are
detailed in the `WordLexicon` unit document.

#### Sense Catalog Resolves Senses and Groups

The senses of a lemma and part of speech are listed most frequent first; a sense is found by its readable id and by its
OEWN sense key (the same sense through either), a project sense has no key, and every sense is in at most one group
that lists it, every group having at least two senses and never all the senses of its lemma and part of speech, and
every group id carrying the group marker (`lemma#group:label`) and never equal to a sense id; the version string of the
inventory is the same on every call and has the form of 16 lower-case hexadecimal digits. The
scenarios are detailed in the `WordLexicon` unit document.

#### Contracts Are Usable Without a Model Package

`ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`, `PartOfSpeech`, `SenseInfo`,
`SenseGroup`, `MirrorOptions`, `IWordSenseModel` and `FallbackWordSenseModel` can be used by a test double with no
model package referenced (`ContractsTests`). The scenarios are
detailed in the `WordSenseContracts` unit document.

#### No ONNX Dependency

The package does not reference ONNX Runtime or any other WordSense package; the automated test
`WordSenseContracts_CoreAssembly_HasNoOnnxOrWeightsReferences` (`ContractsTests`) checks the assembly references, and it
is why the core test project runs without model files.

#### XML Documentation Examples Compile

Every `<code>` example in the shipped XML documentation file (`DemaConsulting.WordSense.xml`) is extracted and compiled
against the real public API, so a documented example cannot drift from the API (`XmlDocExampleTests`).
