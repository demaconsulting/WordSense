# Introduction

This document provides the detailed design for the WordSense library, a .NET library that tells the part of
speech and pronunciation of English words in text. Words that have one sense are answered from an embedded
lexicon; words that have several senses are answered in context by an interchangeable word-sense model.

## Purpose

The purpose of this document is to serve as the design entry point and provide detailed design
specifications for the WordSense system. This documentation enables formal code
review by providing implementation specifications, supports compliance auditing by maintaining
clear traceability from requirements through design to code, aids maintenance by documenting
system structure and interactions, and ensures quality assurance through detailed technical
specifications.

This document is intended for:

- Software developers implementing and maintaining the system
- Code reviewers validating implementation against design
- Compliance auditors tracing requirements through design to implementation
- Quality assurance teams validating system behavior

## Scope

This document covers the detailed design of the WordSense system and its constituent
software items, specifically:

- **WordSense (System)** — the whole library, delivered as five layered NuGet packages
- **Core (Subsystem)** — the package `DemaConsulting.WordSense`: the embedded word lexicon and the public contracts;
  it has no third-party dependency
  - **WordLexicon (Unit)** — the embedded lexicon of known words and their senses, including the internal reader of
    the lexicon blob
  - **WordSenseContracts (Unit)** — the model and classifier interfaces, the mirror options and the answer values
- **Basic (Subsystem)** — the package `DemaConsulting.WordSense.Basic`: a statistical model (part-of-speech tagger and
  context cues) that needs no model files
  - **BasicWordSenseModel (Unit)** — the public statistical model
  - **BasicClassifier (Unit)** — lexicon lookup, sentence segmentation and the driving of the statistical units
  - **BasicTables (Unit)** — the embedded statistics table and its reader
  - **PosTagger (Unit)** — the hidden Markov model part-of-speech tagger
  - **CueModel (Unit)** — the context features and the naive Bayes cue scores
  - **SenseFusion (Unit)** — the combination into sense probabilities and confidence
- **Onnx (Subsystem)** — the package `DemaConsulting.WordSense.Onnx`: the machinery shared by the encoder models
  - **OnnxWordSenseModel (Unit)** — the public abstract encoder model with file management
  - **OnnxClassifier (Unit)** — scoring of the candidate senses of a word from the encoder output
  - **OnnxEncoder (Unit)** — ONNX inference session and mean pooling per word
  - **WordPieceTokenizer (Unit)** — BERT WordPiece tokenizer, uncased or cased
  - **ModelDownloader (Unit)** — ordered, verified download of model files
  - **DownloadMirror (Unit)** — validated mirror configuration
  - **LemmaWeights (Unit)** — per-lemma weights, their blob reader, matching to the lexicon and the feature
    computation
- **MiniLM (Subsystem)** — the package `DemaConsulting.WordSense.MiniLM`
  - **MiniLMWordSenseModel (Unit)** — the all-MiniLM-L6-v2 int8 model
- **BertBaseCased (Subsystem)** — the package `DemaConsulting.WordSense.BertBaseCased`
  - **BertBaseCasedWordSenseModel (Unit)** — the bert-base-cased int8 model
- **WordSenseBuilder (Unit)** — the non-shipped build-time tool that creates the lexicon blob and the weights blobs
  and measures the models

The ONNX Runtime OTS runtime dependency of the Onnx package is also covered. The core package and the Basic package
have no third-party runtime dependency.

OTS items are intentionally outside the SysML2 model: the model describes only the software items of
WordSense (the system, its subsystems and units), while the OTS items are documented by their own design,
requirements and verification documents.

The following OTS items are also covered:

- **ApiMark** — API documentation generation tool
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **OnnxRuntime** — ONNX inference engine used by the Onnx package
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model lint and diagram rendering tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

Version applicability: This design applies to all versions of the WordSense.

The following topics are explicitly excluded from this design documentation:

- External library internals and third-party OTS components
- Build pipeline configuration and CI/CD processes
- Deployment, packaging, and distribution mechanisms
- Infrastructure and hosting environment details
- Test projects and test infrastructure

## Software Structure

The software structure is modeled in SysML2 under `docs/sysml2/` and rendered to the
diagram below by SysML2Tools as part of the build pipeline. AI agents should query the
SysML2 model directly (see the `sysml2tools-query` skill) rather than parsing this
diagram or the prose below.

![Software Structure](SoftwareStructureView.svg)

WordSense ships as five NuGet packages, each modeled as one subsystem of the single WordSense system. The packages
form layers:

- The **core** package defines the vocabulary: the lexicon of known words and the contracts (`IWordSenseModel`,
  `WordSense`, `WordDetails`, `PartOfSpeech`, `MirrorOptions`). It holds no vectors, no
  weights and no ONNX code.
- The **Basic** package implements the contracts with a statistical tagger and cue model. It references the core
  package only.
- The **Onnx** package implements the contracts for any BERT-style encoder: model file management, encoder,
  tokenizer, weights and scoring. It references the core package and ONNX Runtime.
- The **MiniLM** and **BertBaseCased** packages each add one public class that pins one encoder model and embeds the
  weights trained for it. They reference the Onnx package.

The dependency rule is that a package may reference only the packages in layers below it: core references nothing,
Basic and Onnx reference core, and MiniLM and BertBaseCased reference Onnx (and through it core). No package
references a sibling model package. The builder tool references all five packages and is never shipped.

![WordSense system](WordSenseView.svg)

The data flows from the contracts upward: a caller obtains an `IWordSenseModel` from a model package, calls
`EnsureModelAsync` once (optional: `ClassifyAsync` ensures lazily when a word needs the model) and classifies
tokenized text. Every model looks each
word up in `WordLexicon` first; only a word with several senses needs the model's context decision.

## Folder Layout

The source code folder structure mirrors the packages, and the packages mirror the subsystems:

```text
src/DemaConsulting.WordSense/                      — core subsystem
├── WordLexicon.cs, WordsBlob.cs                    — word-lexicon unit
├── IWordSenseModel.cs (also WordSense, MirrorOptions), WordDetails.cs (also
│   PartOfSpeech)                                   — word-sense-contracts unit
├── NamespaceDoc.cs                                 — namespace description for the generated API reference (no code)
└── Resources/words.bin                             — embedded lexicon blob (resource of the word-lexicon unit)

src/DemaConsulting.WordSense.Basic/                — basic subsystem
├── BasicWordSenseModel.cs                          — basic-word-sense-model unit
├── BasicClassifier.cs                              — basic-classifier unit
├── BasicTables.cs                                  — basic-tables unit (tables and the feature hash)
├── PosTag.cs                                       — tag constants of the basic-tables and pos-tagger units
├── PosTagger.cs                                    — pos-tagger unit
├── CueModel.cs                                     — cue-model unit
├── SenseFusion.cs                                  — sense-fusion unit
├── NamespaceDoc.cs
└── Resources/basic.bin                             — embedded statistics table (resource of the basic-tables unit)

src/DemaConsulting.WordSense.Onnx/                 — onnx subsystem
├── OnnxWordSenseModel.cs                           — onnx-word-sense-model unit
├── OnnxClassifier.cs                               — onnx-classifier unit
├── OnnxEncoder.cs                                  — onnx-encoder unit
├── WordPieceTokenizer.cs, TokenizedText.cs         — word-piece-tokenizer unit
├── ModelDownloader.cs (also ModelSpec, ModelFile)  — model-downloader unit
├── DownloadMirror.cs                               — download-mirror unit
├── LemmaWeights.cs, WeightsBlob.cs, WeightedLemmaTable.cs, FeatureExtractor.cs, VectorMath.cs — lemma-weights unit
└── NamespaceDoc.cs

src/DemaConsulting.WordSense.MiniLM/               — mini-lm subsystem
├── MiniLMWordSenseModel.cs                         — mini-lm-word-sense-model unit
├── NamespaceDoc.cs
└── Resources/weights.bin                           — embedded weights of the MiniLM model

src/DemaConsulting.WordSense.BertBaseCased/        — bert-base-cased subsystem
├── BertBaseCasedWordSenseModel.cs                  — bert-base-cased-word-sense-model unit
├── NamespaceDoc.cs
└── Resources/weights.bin                           — embedded weights of the bert-base-cased model

model/<model-id>/                                  — model files (ONNX file, vocabulary, tokenizer and config files,
                                                      SHA256SUMS) of each encoder model
tools/WordSense.Builder/                           — word-sense-builder unit (not shipped)
corpus/                                            — builder inputs: sense inventory, anchor, holdout and form
                                                      sentences, dictionary lexicon, provenance
```

The `generated/` folders of the packages hold the generated API reference and are not committed. The corpus files
are data inputs of the builder unit; the word-lexicon unit holds the compiled result, so no separate unit exists for
the dictionary. The model files under `model/<model-id>/` are the same files that a mirror serves as
`<mirror>/<model-id>/<file>` and that the per-user store keeps as `<store>/<model-id>/`.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: purpose, data model, key methods,
  error handling, dependencies and callers.
- Subsystem chapters are in the folder `word-sense/`, and the unit chapters of each subsystem are in the folder of
  the same name below it (for example `word-sense/onnx/`). The builder unit chapter is directly in `word-sense/`.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Each software item has corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/[{subsystem}/]{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/[{subsystem}/]{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/[{subsystem}/]{item}.md` (kebab-case)
- Source code: `src/{PackageName}/{Item}.cs` (PascalCase for C#); the package folder is the subsystem
- Tests: `test/{PackageName}.Tests/{Item}Tests.cs` (PascalCase for C#); the builder tests are in
  `test/WordSense.Builder.Tests/`
- SysML2 model: `docs/sysml2/model/{system}/[{subsystem}/]{item}.sysml` (kebab-case)
- Review-sets: defined in `.reviewmark.yaml`

For example, the unit `LemmaWeights` of the Onnx subsystem has the requirements file
`docs/reqstream/word-sense/onnx/lemma-weights.yaml`, the design `docs/design/word-sense/onnx/lemma-weights.md`, the
verification design `docs/verification/word-sense/onnx/lemma-weights.md`, the sources in
`src/DemaConsulting.WordSense.Onnx/` and the tests in `test/DemaConsulting.WordSense.Onnx.Tests/`.

## References

- WordSense User Guide — the compiled User Guide document for this repository.
- WordSense Repository — the WordSense source repository hosted on
  GitHub.
