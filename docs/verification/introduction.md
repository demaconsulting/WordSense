# Introduction

This document provides the verification design for the WordSense, a .NET library
that identifies the sense of ambiguous English words in context (the part of speech and, for heteronyms, a sense id
that fixes the pronunciation) and the part of speech of
common English words from an embedded dictionary.

## Purpose

The purpose of this document is to serve as the verification design entry point and document how
requirements will be tested across all software items in the WordSense system. This
documentation enables formal review by mapping every requirement to named test scenarios, supports
compliance auditing by providing clear traceability from requirements through verification design
to tests, and ensures test completeness can be assessed without reading implementation code.

This document is intended for:

- Software developers implementing and maintaining tests
- Code reviewers validating test completeness against requirements
- Compliance auditors tracing requirements through verification design to tests
- Quality assurance teams validating test coverage and scenario adequacy

## Scope

This document covers the verification design for the WordSense system and its
constituent software items, specifically:

- **WordSense (System)** — The five layered packages of WordSense, which the units and subsystems below make up
- **WordSenseBuilder** (non-shipped tool Unit) of the system, which generates the embedded lexicon, weights and appendix
- **Core**, **Basic**, **Onnx**, **MiniLM** and **BertBaseCased** (Subsystems) — the packages `DemaConsulting.WordSense`,
  `DemaConsulting.WordSense.Basic`, `DemaConsulting.WordSense.Onnx`, `DemaConsulting.WordSense.MiniLM` and
  `DemaConsulting.WordSense.BertBaseCased`
- **WordLexicon** and **WordSenseContracts** (Units) of the Core subsystem
- **BasicWordSenseModel**, **BasicClassifier**, **BasicTables**, **PosTagger**, **CueModel** and **SenseFusion**
  (Units) of
  the Basic subsystem
- **OnnxWordSenseModel**, **OnnxClassifier**, **OnnxEncoder**, **WordPieceTokenizer**, **ModelDownloader**,
  **DownloadMirror** and **LemmaWeights** (Units) of the Onnx subsystem
- **MiniLMWordSenseModel** (Unit) of the MiniLM subsystem
- **BertBaseCasedWordSenseModel** (Unit) of the BertBaseCased subsystem
- **WordData** (Subsystem) and its units **DataSchema**, **DataLayout**, **OewnImporter** and **DataValidator**

The OnnxRuntime OTS runtime dependency of the Onnx package is also covered.

The following OTS items are also covered:

- **ApiMark** — API documentation generation tool
- **BuildMark** — build-notes documentation tool
- **FileAssert** — document assertion tool
- **Pandoc** — Markdown-to-HTML conversion tool
- **ReqStream** — requirements traceability tool
- **ReviewMark** — file review enforcement tool
- **SarifMark** — SARIF report conversion tool
- **SonarMark** — SonarCloud quality report tool
- **SysML2Tools** — architecture model tool
- **VersionMark** — tool-version documentation tool
- **WeasyPrint** — HTML-to-PDF conversion tool
- **xUnit** — unit-testing framework

This verification documentation covers the same software items as the design documentation.

Version applicability: This verification design applies to all versions of the WordSense.

The following topics are explicitly excluded from this verification documentation:

- Build pipeline and CI/CD process testing
- Infrastructure and hosting environment testing

## Companion Artifact Structure

Each software item covered by this document has corresponding artifacts in parallel directory
trees. In-house items have artifacts in these parallel locations:

- Requirements: `docs/reqstream/{system}/.../{item}.yaml` (kebab-case)
- Design docs: `docs/design/{system}/.../{item}.md` (kebab-case)
- Verification design: `docs/verification/{system}/.../{item}.md` (kebab-case)
- Source code: `src/{Package}/.../{Item}.cs` (PascalCase for C#; the package folders are `DemaConsulting.WordSense`,
  `DemaConsulting.WordSense.Basic`, `DemaConsulting.WordSense.Onnx`, `DemaConsulting.WordSense.MiniLM` and
  `DemaConsulting.WordSense.BertBaseCased`, and the builder is `tools/WordSense.Builder`)
- Tests: `test/{Package}.Tests/.../{Item}Tests.cs` (PascalCase for C#; the builder tests are in
  `test/WordSense.Builder.Tests`)

OTS items have parallel artifacts in:

- Requirements: `docs/reqstream/ots/{ots-name}.yaml` (kebab-case)
- Verification: `docs/verification/ots/{ots-name}.md` (kebab-case)

Review-sets: defined in `.reviewmark.yaml`

## References

- WordSense User Guide — the compiled User Guide document for this repository.
- WordSense Repository — the WordSense source repository hosted on
  GitHub.
