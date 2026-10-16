# Introduction

This document provides the detailed design for the WordSense library, a .NET library
that classifies the sense of ambiguous English words in context.

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

- **WordSense (System)** — The complete library, shipped as the single package DemaConsulting.WordSense
- **WordPieceTokenizer (Unit)** — BERT uncased word-piece tokenizer
- **WordSenseTable (Unit)** — embedded binary classifier table
- **SenseRecord (Unit)** — immutable tagged sense records and tag scoring
- **OnnxWordSenseEncoder (Unit)** — ONNX Runtime encoder
- **WordSenseClassifier (Unit)** — public classification entry point
- **DownloadMirror (Unit)** — user mirror configuration
- **WordSenseModelDownloader (Unit)** — ordered, verified model download
- **WordSenseBuilder (Unit)** — non-shipped table builder tool

The OnnxRuntime OTS runtime dependency is also covered.

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

WordSense has no subsystems; its units sit directly under the system. There is one shipped assembly,
`DemaConsulting.WordSense`, so tokenizer, table, encoder, classifier and model management share a
package and the only runtime dependency is ONNX Runtime.

## Folder Layout

The source code folder structure mirrors the software structure organization, with file paths
and descriptions as follows:

```text
src/DemaConsulting.WordSense/
├── WordPieceTokenizer.cs, TokenizedText.cs        — tokenizer unit
├── WordSenseTable.cs, LemmaModel.cs, VectorMath.cs — table unit
├── WordSense.cs, SenseDefinition.cs, TagScorer.cs, TextSplitter.cs, WordSenseToken.cs — sense record unit
├── OnnxWordSenseEncoder.cs, IWordSenseEncoder.cs  — encoder unit
├── WordSenseClassifier.cs, FeatureExtractor.cs    — classifier unit
├── DownloadMirror.cs                              — mirror unit
├── WordSenseModelDownloader*.cs, ...ModelCatalog.cs — downloader unit
├── NamespaceDoc.cs                                — namespace description for the generated API reference (no code)
└── Resources/wordsense-table.bin                  — embedded table

tools/WordSense.Builder/                           — builder unit (not shipped)
```

The folder structure is flat because the system has no subsystems.

## Document Conventions

Throughout this document:

- Class names, method names, property names, and file names appear in `monospace` font.
- The word **shall** denotes a design constraint that the implementation must satisfy.
- Section headings within each unit chapter follow a consistent structure: overview, data model,
  methods/algorithms, and interactions with other units.
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
