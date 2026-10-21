# Formal Review Backlog

This note records the findings of the first formal review of the fine-sense work (pull request 14) that were not fixed in
that pull request. All 33 review-sets were reviewed once and every set was rejected, mainly for repository-wide
compliance debt that no single change can clear. The items below are the deferred backlog, grouped by theme, with the
review-sets that raised them. The Planned Improvements list of the README refers to this note.

## Requirements and Design

- **Atomic requirements.** Split the compound requirements across the requirement tree into single-statement
  requirements (raised by nearly every set).
- **Implementation in requirements.** Move requirements that prescribe the implementation (the solver, the cache, the
  switch syntax, named classes) into the design documents (WordSense-AllRequirements, WordSense-Core,
  WordSense-Onnx).
- **Method-level contracts.** Add typed method contracts to the unit designs of MiniLM, the Onnx classifier and model, the
  Basic model and the form generator (WordSense-Design, WordSense-MiniLM, WordSense-Onnx, WordSense-WordData).
- **References sections.** Stop listing internal artifacts in References sections (WordSense-Design).

## Decomposition and Architecture

- Separate the runtime system from the build-time tool and data, model the builder as a subsystem with units, give each
  unit non-overlapping source references, model the OTS items in SysML2 and add the build-time relationships
  (Decomposition, WordSense-Architecture).

## Test Evidence

- **Test structure.** Label the Arrange, Act and Assert steps in all tests (245 of the 434 builder tests and most of the
  others) and document private members and test helpers in XML (WordSense-Verification and most code sets).
- **Traceability level.** Link system requirements to system-level tests and subsystem requirements to subsystem-level
  tests, add Onnx subsystem tests and the BertBaseCased suite links, and move the unit-level links down
  (WordSense-Verification, WordSense-AllRequirements).
- **Skipped model tests.** The model-backed tests skip when the Git LFS assets are missing; make the CI acceptance run
  fail on skips (WordSense-Verification, WordSense-Onnx, WordSense-MiniLM, WordSense-BertBaseCased).
- **Packages.** Add pack-and-inspect tests for the package metadata, readme, notice, license expressions and attribution
  of all four packages, and a negative `EnforceDocs` fixture for ApiMark (WordSense-Verification, OTS-ApiMark).
- **Missing direct tests.** Add tests for `FeatureExtractor` and `VectorMath`, the default MiniLM sequence length, the
  previous-release mapping edge cases of the OEWN importer, and enumeration tests of the catalog-wide invariants.

## Data and Word Image Hardening

- Check the overlap and flags of the image directories, make `BasicTables` immutable, check the lambda-sum invariant,
  reject trailing bytes after the Brotli stream and reject positive cue priors (WordSense-Core-WordLexicon,
  WordSense-Core-BasicTables, WordSense-Core-CueModel). This joins the existing word image hardening item.
- Regenerate the form table and the word image to drop the duplicate `supposed` adjective analysis (rank 2 in
  `data/form/su.tsv`). The duplicate is already gone from `corpus/lexicon/form-frozen.tsv` and the form generator now
  rejects such duplicates, but the new image changes the inventory hash, so the Basic tables and both sets of encoder
  weights must be retrained with it (WordSense-WordData-FormGenerator).
- Use the full digest as the inventory version and make the harvest script `harvest.py` deterministic in its retrieval
  date (WordSense-Onnx-LemmaWeights, WordSense-WordData).

## Evaluation and Reproducibility

- Automate the reproducibility check of the real weights, tighten the tolerance of the README samples, specify the cue
  window, set numeric floors for the challenge sets and keep per-platform build evidence (WordSense-Verification,
  WordSense-Onnx).
- Add an explicit requirement for the retirement of the Basic package (WordSense-AllRequirements).
