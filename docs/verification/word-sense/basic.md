## Basic Subsystem Verification Design

This document describes the verification of the Basic subsystem, the package `DemaConsulting.WordSense.Basic`: a
model that needs no model files and chooses among the senses of a word with hand-written rules.

### Verification Approach

The subsystem is verified through the public `BasicWordSenseModel` and through the internal `BasicClassifier` and
`Rules` units, with the real embedded lexicon of the core package (the rules need real part-of-speech sets of the
neighbors). Nothing is mocked: the model has no files, no network access and no state. Tests in
`test/DemaConsulting.WordSense.Basic.Tests` (`BasicWordSenseModelTests`, `BasicClassifierTests`, `RulesTests`) verify the
three units, and `XmlDocExampleTests` compiles the XML documentation examples. `BasicReadmeSampleTests` compiles and runs
the Basic usage sample of the README and compares its code and output with the README.

### Test Environment

Standard xUnit v3 runner on net8.0, net9.0 and net10.0. No model files, no network access and no environment variables
are needed.

### Acceptance Criteria

All subsystem scenarios pass on every platform and target framework of the CI matrix, and the unit scenarios of the
`BasicWordSenseModel`, `BasicClassifier` and `Rules` units pass.

### Test Scenarios

#### Model Needs No Setup

`EnsureModelAsync` completes immediately without touching the file system or the network, ignores a mirror, and
`Create` returns a working classifier at once (see the `BasicWordSenseModel` unit document).

#### Abstains When No Rule Fires

For a word with several senses and no rule that fires, the result is `(word, null, 0)`: the model abstains instead of
guessing (see the `BasicClassifier` and `Rules` unit documents).

#### Eight Rules Apply in Order

The rules `after-modal`, `after-to`, `after-subject-pronoun`, `after-determiner`, `after-degree-adverb`,
`after-preposition`, `before-auxiliary` and `before-noun` are tried in this order and the first that fires decides (see
the `Rules` unit document).

#### Unambiguous Words Match the Other Packages

A word with a single sense is answered with the lexicon entry and confidence 1, and an unknown word is `(word, null,
0)`, exactly as in the model packages.

#### XML Documentation Examples Compile

Every `<code>` example of the shipped XML documentation file is extracted and compiled against the real public API
(`XmlDocExampleTests`).
