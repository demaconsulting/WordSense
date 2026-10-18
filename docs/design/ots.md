# OTS Integration Design

This document describes the overall Off-The-Shelf (OTS) integration strategy for the WordSense repository.

## Overview

The WordSense core package and the Basic package have no third-party runtime dependency. The Onnx package,
`DemaConsulting.WordSense.Onnx`, has exactly one third-party runtime NuGet dependency, Microsoft.ML.OnnxRuntime,
which is shipped with the Onnx package as a dependency; the MiniLM and BertBaseCased packages receive it through the
Onnx package. All other OTS items listed below are
build-time and quality-pipeline tools, not runtime library dependencies. Each of those items provides one
stage of the documentation, requirements-traceability, testing, and quality-reporting pipeline
invoked by `build.ps1`, `lint.ps1`, and the `.github/workflows/build.yaml` CI workflow. None of
these tools are linked into, or shipped with, the compiled NuGet packages; ONNX Runtime is the
exception, and only for the Onnx package and the packages built on it.

## OTS Items

| OTS Item    | Purpose                                                              |
|-------------|----------------------------------------------------------------------|
| ApiMark     | Generates the API reference from XML docs and enforces doc coverage  |
| BuildMark   | Generates build-notes documentation from GitHub Actions metadata     |
| FileAssert  | Validates generated documents (HTML/PDF) against acceptance criteria |
| OnnxRuntime | Onnx package runtime dependency: executes the quantized models       |
| Pandoc      | Converts Markdown documentation to HTML                              |
| ReqStream   | Enforces requirements-to-test traceability                           |
| ReviewMark  | Enforces file review coverage and currency                           |
| SarifMark   | Converts CodeQL SARIF results into a markdown report                 |
| SonarMark   | Generates a SonarCloud quality report                                |
| SysML2Tools | Validates the SysML2 architecture model and renders its views to SVG |
| VersionMark | Captures and publishes tool-version information                      |
| WeasyPrint  | Converts HTML documentation to PDF                                   |
| xUnit       | Discovers and executes unit and integration tests                    |

Each item's individual design document (`docs/design/ots/{ots-name}.md`) records its Purpose,
Features Used, and Integration Pattern. Each item's requirements and verification evidence are
recorded in `docs/reqstream/ots/{ots-name}.yaml` and `docs/verification/ots/{ots-name}.md`
respectively.
