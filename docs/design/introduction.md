# Introduction

This document provides the detailed design for the WordSense library, a .NET library
that tells the part of speech and pronunciation of English words in text, answering words that have one part of
speech from an embedded dictionary and ambiguous words in context.

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

- **WordSense (System)** — The core package DemaConsulting.WordSense: the lexicon, the model abstraction and the
  classifier; it has no runtime dependency
- **WordSenseModel (Unit)** — the model abstraction (`IWordSenseModel`) that the classifier uses to embed words
- **WordSenseTable (Unit)** — embedded lexicon table of lemma entries, forms and the resolver, and the per-model
  weights table
- **SenseRecord (Unit)** — immutable answer values, answer basis, sense definition validation and abstention rule
- **WordSenseClassifier (Unit)** — public classification entry point, with or without a model
- **WordSenseBuilder (Unit)** — non-shipped table builder tool
- **WordSenseMiniLM (System)** — The package DemaConsulting.WordSense.MiniLM: the MiniLM model, its model
  management and the embedded weights; it depends on the core package and on ONNX Runtime
- **MiniLMWordSense (Unit)** — factory that assembles a model-backed classifier and loads the embedded weights
- **MiniLMWordSenseModel (Unit)** — ONNX Runtime implementation of the model abstraction
- **WordPieceTokenizer (Unit)** — BERT uncased word-piece tokenizer
- **DownloadMirror (Unit)** — user mirror configuration
- **WordSenseModelDownloader (Unit)** — ordered, verified model download

The OnnxRuntime OTS runtime dependency of the MiniLM package is also covered.

A shared package for ONNX-based models (a `WordSense.Onnx` package) is intentionally not created yet. With one
model, the ONNX session handling, tokenizer and model management stay in the MiniLM package; the shared package
is deferred until a second model exists and shows which parts the models really have in common.

OTS items are intentionally outside the SysML2 model: the model describes only the software items of
WordSense (the system and its units), while the OTS items are documented by their own design, requirements
and verification documents.

The following OTS items are also covered:

- **ApiMark** — API documentation generation tool
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
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

WordSense ships as two packages, each modeled as a system with no subsystems; its units sit directly under it.
The core system `DemaConsulting.WordSense` has no runtime dependency and answers every word that its lexicon
settles. The MiniLM system `DemaConsulting.WordSense.MiniLM` adds a model that resolves the contextual words and
carries the only runtime dependency, ONNX Runtime. The MiniLM system uses the core system; the core system does not
know it.

![WordSense core system](WordSenseView.svg)

![WordSense MiniLM system](WordSenseMiniLMView.svg)

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.WordSense/                      — core system
├── IWordSenseModel.cs, WordSenseModelMismatchException.cs — model abstraction unit
├── WordSenseTable.cs, WordSenseWeightsTable.cs, LemmaEntry.cs, SenseWeights.cs, LexicalIndex.cs,
│   LemmaMorphology.cs, LemmaResolution.cs, TableReader.cs, VectorMath.cs,
│   WordSenseTableFormatException.cs                — table unit
├── PartOfSpeech.cs, Pronunciation.cs, WordDetails.cs, WordAnalysis.cs, AnswerBasis.cs, SenseDefinition.cs,
│   AbstainRule.cs, TextSplitter.cs                 — sense record unit
├── WordSenseClassifier.cs (also declares IWordSenseClassifier and WordSenseToken),
│   WordSenseClassifierOptions.cs, FeatureExtractor.cs — classifier unit
├── NamespaceDoc.cs                                — namespace description for the generated API reference (no code)
├── generated/                                     — generated API reference (not committed)
└── Resources/wordsense-lexicon.bin                — embedded lexicon (context lemmas, forms and dictionary)

src/DemaConsulting.WordSense.MiniLM/               — MiniLM system
├── MiniLMWordSense.cs, MiniLMWordSenseOptions.cs  — factory unit
├── MiniLMWordSenseModel.cs                        — model unit
├── WordPieceTokenizer.cs, TokenizedText.cs        — tokenizer unit
├── DownloadMirror.cs                              — mirror unit
├── WordSenseModelDownloader.cs, WordSenseModelLocation.cs, WordSenseModelCatalog.cs — downloader unit
├── WordSenseModelDownloaderOptions.cs             — downloader unit; also declares WordSenseModelDownloadProgress
│                                                    and WordSenseModelDownloadException
├── NamespaceDoc.cs                                — namespace description for the generated API reference (no code)
├── generated/                                     — generated API reference (not committed)
└── Resources/weights.<model-id>.<onnx-sha256>.bin — embedded weights of the model, keyed by model id and the
                                                     SHA-256 of its ONNX file

model/<model-id>/                                  — model files (ONNX file, vocabulary, tokenizer file, SHA256SUMS)

tools/WordSense.Builder/                           — builder unit (not shipped)
corpus/lexicon/                                    — lexicon, corrections, classes, gated words, morphology rules and
                                                     provenance (builder unit input)
```

The folder structure is flat because the systems have no subsystems. The lexicon files are data inputs of the
builder unit; the table unit holds the compiled result, so no separate unit exists for the dictionary. The model
files under `model/<model-id>/` are the same files that a mirror serves as `<mirror>/<model-id>/<file>` and that
the per-user store keeps as `<store>/<model-id>/`.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods (or algorithm), error handling, and interactions with other units; a unit may add further sections
  where it needs them (for example thread safety).
- Unit chapters of the core system are in the folder `word-sense/` and those of the MiniLM system in the folder
  `word-sense-mini-lm/`.
- Text tables are used in preference to diagrams, which may not render in all PDF viewers.

## Companion Artifact Structure

Each software item has corresponding artifacts in parallel directory trees:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{System}/.../{Item}.cs` (PascalCase for C#)
- Tests: `test/{System}.Tests/.../{Item}Tests.cs` (PascalCase for C#)
- SysML2 model: `docs/sysml2/model/{system}/.../{item}.sysml` (kebab-case)
- Review-sets: defined in `.reviewmark.yaml`

## References

- WordSense User Guide — the compiled User Guide document for this repository.
- WordSense Repository — the WordSense source repository hosted on
  GitHub.
