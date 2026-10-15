# Formal Review: WordSense-WordSenseBuilder (round 4)

## 1. Introduction

Review of the review set `WordSense-WordSenseBuilder` on branch `feature/initial-wordsense` after the round-3 fixes
(commit f0094f2). Template: ContinuousCompliance `review-template.md` (main). Fingerprint
`86df4f002c7c474e49cf15440bf11d8121eb0ebd2de28eb80743817f669d666a`. Date 2026-10-15.
Files: the 29 files listed by `dotnet reviewmark --elaborate WordSense-WordSenseBuilder`; context files
`introduction.md`, `word-sense.md` and `word-sense.yaml` were read first.

Guidance applied: no compound-requirement findings; the choice not to catch `KeyNotFoundException` and
`IndexOutOfRangeException` in the builder CLI is intentional and not reported.

## 2. Checklist (summary)

| Group | Outcome | Note |
| :---- | :------ | :--- |
| REQ-01..08 | Pass | Every requirement has an id and tests; every listed test exists. |
| DES-01..05 | Pass | The design now has Error Handling and Key Methods and Callers sections (round-3 F-4 closed). |
| DOC-01..04 | Pass | The web-excerpt wording and the `Candidate.Source` list are fixed (F-2 closed). |
| CODE / LOGIC / ERR / READ / SEC | Pass, with F-1 and F-2 | See findings. |
| REQDOC / REQIMP | Pass | |
| TEST-01..05, REQTEST-01..02 | Pass | The import path now has tests (F-1 closed). |
| CODEDOC-01..04 | Fail | F-1, F-2 below. |

Round-3 findings: F-1 (tests) closed. F-2 closed. F-3 is now documented in the design (lemma-level semantics) and
closed. F-4 closed. F-5: `UnauthorizedAccessException` is caught, required decisions are read before any write, and
the `.out` path replacement is fixed. The two exception types left out are intentional. F-6 is closed for `rows.tsv`,
the judge inputs and `dropped-rows.tsv`, with the gaps listed below.

## 3. Findings

### F-1 (Low) Design and test claim "file and line" for column errors, but some readers do not name the line
- Files: `docs/design/word-sense/word-sense-builder.md` (Error Handling, last bullet),
  `docs/verification/word-sense/word-sense-builder.md` (`..._MalformedRows_AreDataErrors`),
  `tools/WordSense.Builder/RealPlan.cs` (`ReadPool`), `tools/WordSense.Builder/RealJudge.cs` (`ReadCandidates`).
- The design says that readers of `dropped-rows.tsv` (among others) reject a row with the wrong number of columns
  with an `InvalidDataException` naming the file and line, and the verification repeats this. `ReadPool` reports the
  file name and the row's content, not its line number. `ReadCandidates` (the candidates file) does the same.
- Recommendation: add the line number to both messages, or soften the design and verification wording to "names the
  file and the offending row".

### F-2 (Low) Design says every column-count error is rejected, but `UpdateDecisions` skips malformed rows
- Files: `tools/WordSense.Builder/RealImport.cs` (`UpdateDecisions`), `docs/design/word-sense/word-sense-builder.md`.
- `UpdateDecisions` updates a row only when it has exactly 9 columns. A row with a different column count in
  `relabel-decisions.tsv` is left unchanged and no error is raised. `RealJudge.ParseAnswers` accepts 3 or 4 columns
  and the judge input reader requires at least 3, so the design's "wrong number of columns" wording is not exact
  for these readers.
- Recommendation: throw `InvalidDataException` naming file and line for a malformed decisions row, and state the
  accepted column ranges in the design.

## 4. Conclusion

**Result: FAILED.** There are 0 Critical, 0 High and 0 Medium findings and 2 Low findings (F-1, F-2), both in the
consistency of the error-handling description with the code. All round-3 Medium findings are closed.
