# Introduction

This document provides the detailed design for the WordSense library, a .NET library that identifies the sense of
English words in text: the lemma, the part of speech and, where the model can tell, the sense: an Open English WordNet
(OEWN) fine sense with a readable id, or the group of closely related, easily confused senses that it belongs to where
one exists. Every answer states the level
(part of speech, group or sense) at which it was made and lists the ranked analyses of the word and, where the model
scored them, the ranked senses of each analysis. Words that have one sense are answered from an embedded
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

- **WordSense (System)** — the whole library, delivered as four layered NuGet packages
- **Core (Subsystem)** — the package `DemaConsulting.WordSense`: the embedded word lexicon and sense catalog, the
  public model interface and the built-in statistical model (the former Basic package); it has no third-party
  dependency
  - **WordLexicon (Unit)** — the embedded lexicon of known words (their analyses) and the sense catalog (senses and
    groups), including the internal reader of the lexicon blob
  - **WordSenseContracts (Unit)** — the model interface, the composite `FallbackWordSenseModel`, the mirror options,
    the answer values (`ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`), the answer level and the sense and
    group records
  - **BasicWordSenseModel (Unit)** — the public statistical model
  - **BasicClassifier (Unit)** — lexicon lookup, sentence segmentation and the driving of the statistical units
  - **BasicTables (Unit)** — the embedded statistics table and its reader
  - **PosTagger (Unit)** — the hidden Markov model part-of-speech tagger
  - **CueModel (Unit)** — the context features and the naive Bayes cue scores
  - **SenseFusion (Unit)** — the combination into analysis probabilities and confidence
- **Onnx (Subsystem)** — the package `DemaConsulting.WordSense.Onnx`: the machinery shared by the encoder models; it
  references only the core package
  - **OnnxWordSenseModel (Unit)** — the public abstract encoder model with file management, usable on its own
  - **OnnxClassifier (Unit)** — scoring of the fine senses of a word from the encoder output, the answer level, the
    form chooser and the lexicon-only answer of a word without scored senses
  - **OnnxEncoder (Unit)** — ONNX inference session and mean pooling per word
  - **WordPieceTokenizer (Unit)** — BERT WordPiece tokenizer, uncased or cased
  - **ModelDownloader (Unit)** — ordered, verified download of model files
  - **DownloadMirror (Unit)** — validated mirror configuration
  - **LemmaWeights (Unit)** — per-lemma weights and thresholds, their blob reader, matching to the lexicon and the
    feature computation
- **MiniLM (Subsystem)** — the package `DemaConsulting.WordSense.MiniLM`; it references the core and Onnx packages
  - **MiniLMWordSenseModel (Unit)** — the all-MiniLM-L6-v2 int8 model, its encoder model composed with the core Basic
    model
- **BertBaseCased (Subsystem)** — the package `DemaConsulting.WordSense.BertBaseCased`; it references the core and Onnx
  packages
  - **BertBaseCasedWordSenseModel (Unit)** — the bert-base-cased int8 model, its encoder model composed with the core
    Basic model
- **WordSenseBuilder (Unit)** — the non-shipped build-time tool that creates the lexicon blob and the weights blobs,
  chooses the thresholds and measures the models
- **WordData (Subsystem)** — the TSV word data under `data/` (imported from Open English WordNet, CC BY 4.0, with the
  WordNet 3.0 sense counts for the usage prior) and the non-shipped builder code that imports, labels and validates it
  - **DataSchema**, **DataLayout**, **OewnImporter**, **FormGenerator**, **FormAuditor**, **DataValidator** and
    **SenseLabelGenerator (Units)** — the schema, the file layout, the import, the generated forms and their audit,
    the validation and the readable sense labels

The ONNX Runtime OTS runtime dependency of the Onnx package is also covered. The core package has no third-party
runtime dependency.

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

WordSense ships as four NuGet packages, each modeled as one subsystem of the single WordSense system (the statistical
Basic model, which was a fifth package before version 1.0, is part of the core subsystem). The packages
form layers:

- The **core** package defines the vocabulary: the lexicon of known words with their analyses, the sense catalog (every
  fine sense and, where curated, its group) and the contracts (`IWordSenseModel`, `FallbackWordSenseModel`,
  `ClassifyResult`, `AnalysisCandidate`, `SenseCandidate`, `SenseLevel`, `WordDetails`,
  `PartOfSpeech`, `SenseInfo`, `SenseGroup`, `MirrorOptions`), and it implements the contracts with the built-in
  `BasicWordSenseModel`, a statistical tagger and cue model that answers at the part of speech level (and the sense
  level where the lemma and part of speech have one sense). It holds no encoder weights and no ONNX code. Its
  package license expression is `MIT AND CC-BY-4.0` and a packed `NOTICE.md` carries the attribution of the data
  embedded in it (Open English WordNet 2025 under CC BY 4.0 with the Princeton WordNet notice that it requires) and
  records that the
  statistics of the built-in model come from our own CC0 corpora (the open part-of-speech corpus and the sentence
  corpus) and from no treebank.
- The **Onnx** package implements the contracts for any BERT-style encoder: model file management, encoder,
  tokenizer, weights, thresholds and scoring of the fine senses. It references the core package and ONNX Runtime, and
  nothing else: its abstract encoder model is usable on its own and answers from the lexicon and the encoder alone.
- The **MiniLM** and **BertBaseCased** packages each add one public class that pins one encoder model, embeds the
  weights and thresholds trained for it and composes the encoder model with the core Basic model through the core
  `FallbackWordSenseModel`, so that a word the encoder does not score is answered by the Basic model. They reference
  the core and Onnx packages. Each ships a packed `NOTICE.md` with the Princeton WordNet 3.0 notice for the sense usage
  prior of its weights, the Open English WordNet (CC BY 4.0) attribution for the sense ids the weights hold (their
  license expression is `MIT AND CC-BY-4.0`) and the record that the encoder model files (Apache License 2.0) are
  downloaded from this
  repository, not shipped.

The dependency rule is that a package may reference only the packages in layers below it: core references nothing,
Onnx references core only, and MiniLM and BertBaseCased reference core and Onnx. No package
references a sibling model package other than these two. The builder
tool references all four packages and is never shipped. Further rules of the split:

- The core knows nothing of encoder models: it has no vectors, encoder weights, ONNX or tokenizer, and names no model
  package in code. A model is anything that implements `IWordSenseModel`.
- Weights belong to a model package: each encoder package embeds one `weights.bin` trained for its exact model files and
  checked against their SHA-256 digests. Model files live in `model/<model-id>/`, which also names the mirror folder, the
  per-user store folder and the GitHub LFS path.
- The word table is shared: `WordLexicon` serves the analyses of all known spellings, including the inflections that the
  builder materializes at build time, so no model needs morphology at run time.
- `IWordSenseModel` is `IDisposable`: the model owns the ONNX session and the downloader state and so the lifetime
  (the Basic model honors the same contract and releases nothing). The mirror configuration is the sealed record
  `MirrorOptions` in the core, so every model package takes the same argument; models that need no files ignore it.

![WordSense system](WordSenseView.svg)

The data flows from the contracts upward: a caller obtains an `IWordSenseModel` from a model package, calls
`EnsureModelAsync` once (optional: `ClassifyAsync` ensures lazily when a word needs the model) and classifies
tokenized text, receiving one `ClassifyResult` per word in input order. Every model looks each word up in
`WordLexicon` first; only a word with several senses needs the model's context decision, and each answer states the
level (part of speech, group or sense) at which it was made. The result is built from its `Analyses` (the probability
of every analysis of the word and, where the model scored them, of the senses inside each analysis); the selected
analysis, the level, the confidence and the ids are a summary of it.

## Folder Layout

The source code folder structure mirrors the packages, and the packages mirror the subsystems:

```text
src/DemaConsulting.WordSense/                      — core subsystem
├── WordLexicon.cs, WordImage.cs, ImageFormat.cs   — word-lexicon unit
├── IWordSenseModel.cs (also MirrorOptions), FallbackWordSenseModel.cs, ClassifyResult.cs,
│   AnalysisCandidate.cs, SenseCandidate.cs, WordDetails.cs (also PartOfSpeech), SenseInfo.cs (also SenseGroup),
│   SenseLevel.cs                                   — word-sense-contracts unit
├── BasicWordSenseModel.cs                          — basic-word-sense-model unit
├── BasicClassifier.cs                              — basic-classifier unit
├── BasicTables.cs                                  — basic-tables unit (tables and the feature hash)
├── PosTag.cs                                       — tag constants of the basic-tables and pos-tagger units
├── PosTagger.cs                                    — pos-tagger unit
├── CueModel.cs                                     — cue-model unit
├── SenseFusion.cs                                  — sense-fusion unit
├── NamespaceDoc.cs                                 — namespace description for the generated API reference (no code)
├── Resources/wordsense.img                         — embedded flat word image (resource of the word-lexicon unit)
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
├── MiniLMWordSenseModel.cs, MiniLMEncoderModel.cs  — mini-lm-word-sense-model unit
├── NamespaceDoc.cs
└── Resources/weights.bin                           — embedded weights of the MiniLM model

src/DemaConsulting.WordSense.BertBaseCased/        — bert-base-cased subsystem
├── BertBaseCasedWordSenseModel.cs, BertBaseCasedEncoderModel.cs — bert-base-cased-word-sense-model unit
├── NamespaceDoc.cs
└── Resources/weights.bin                           — embedded weights of the bert-base-cased model

model/<model-id>/                                  — model files (ONNX file, vocabulary, tokenizer and config files,
                                                      SHA256SUMS, the Apache License 2.0 LICENSE) of each encoder
                                                      model
tools/WordSense.Builder/                           — word-sense-builder unit (not shipped)
tools/                                             — the other maintainer tools (not shipped): forms-audit,
                                                      forms-audit, model-prep, quota-fill and tokenizer-fixture;
                                                      tools/README.md describes each tool folder
tools/open-corpus/                                 — the scripts that built the open part-of-speech corpus (Python,
                                                      not shipped; downloads go to its git-ignored cache)
tools/WordSense.Builder/Data/                      — word-data subsystem units (DataSchema.cs and SchemaYaml.cs,
                                                      DataLayout.cs and PartitionResolver.cs, OewnImporter.cs,
                                                      FormGenerator.cs and its helpers, FormAuditor.cs,
                                                      DataValidator.cs, SenseLabelGenerator.cs)
data/                                              — word-data tables (TSV), schema.yaml, README.md, PROVENANCE.md
corpus/                                            — builder inputs: anchor and holdout sentences (fine-sense rows) with
                                                      their provenance (CC0 1.0, public domain in the United States or
                                                      project-authored), dictionary lexicon, the suite
corpus/open/                                       — the open English part-of-speech corpus (CC0 1.0 annotations):
                                                      the representative part, the targeted part and the tag counts
                                                      (corpus/open/counts) that train the Basic tagger
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
