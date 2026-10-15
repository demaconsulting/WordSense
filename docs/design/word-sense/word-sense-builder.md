# WordSenseBuilder Unit Design

## Overview

`tools/WordSense.Builder` is a non-shipped console tool with these commands:
`build --model DIR --corpus DIR --output FILE [--report FILE]`,
`import-batches --source DIR --corpus DIR [--max N]`, the audit commands (`audit-export`, `audit-adjudicate`,
`audit-merge`, `audit-report`, `validate-pos`), `apply-relabel` and the real-text commands (`real-gap`,
`real-judge-export`, `real-judge-adjudicate`, `real-judge-merge`, `real-import`, `real-eval-import`, `real-eval`,
`validate-provenance`) and the analysis commands (`learning-curve --corpus DIR --model DIR --out DIR [--subsets N]
[--seed N] [--lemmas a,b]`, `error-diagnosis --corpus DIR --model DIR --out DIR [--lemmas a,b]` and
`weak-lemmas --analysis DIR [--report FILE]`).

## Data Model

The corpus is tab-separated files under `corpus/anchors` (training) and `corpus/holdout`
(evaluation): `lemma`, `sense`, `sentence` with the target in one `{...}` pair. `corpus/forms/*.tsv`
lists word forms per lemma. Training and evaluation sentences must be disjoint.

## Algorithm

- `Corpus` parses and validates the files; `EnsureDisjoint` rejects duplicates.
- `TableBuilder` encodes every sentence with the real model, pools and centers the target features
  (`FeatureExtractor`), trains one binary or one-versus-rest logistic regression per lemma with
  `LogisticTrainer` (Newton iterations, fixed `C`, backtracking line search, no randomness), picks a
  per-lemma abstention margin by seeded 5-fold cross-validation (lowest threshold with at least 99 %
  answered accuracy and 70 % coverage), evaluates on the holdout and writes the table and a report.
- `BatchImporter` imports a batch only if both files exist (otherwise `Missing`). Lemma-level absence
  (a specified lemma without anchors or holdout sentences) is a warning listed per batch: a lemma without
  anchors has no classifier and is left out, a lemma without holdout is trained but not evaluated.
  Row-level errors (bad format, not exactly one `{...}`, unknown lemma, disallowed label, duplicate
  sentence, anchor/holdout overlap or overlap with the existing corpus) are reported and only the
  offending rows are dropped (the holdout copy of an overlap); if more than 5 % of a batch's rows
  are dropped the batch is `Invalid` and nothing is imported. A lemma left with fewer than two senses,
  or fewer than four anchors for a represented sense, or with holdout rows but no anchors, is excluded
  from both sets and listed. Sentences are never created or edited. Importing is idempotent.
- Sense inventory (`corpus/senses/*.tsv`: `lemma`, `sense`, `tags`, optional `group`) must list exactly the
  senses that the anchors use:
  an anchor label that is not in the inventory, or an inventory sense without anchors, is a validation
  error. `PosRules` fails the build when a sense has no or more than one true part of speech (`noun`, `verb`,
  `adj`, `adv`; qualifiers `past`, `present`, `comparative`, `superlative`, `participle` allowed). Labels of
  `corpus/forms/*.tsv` must be senses of a trained lemma; a forms label without a sense is
  an error, and forms of untrained lemmas are ignored. Every braced target word of the anchor, holdout and
  real-text files must be listed in the forms file (or be a trained lemma) and map to the lemma of its row, because
  the embedded form map is the only way the classifier finds a lemma; the test
  `WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma` enforces this. The only exception is the plural
  `rows` of the lemma `row`: `rows` is itself a trained lemma, so it cannot also map to `row`.
- `BatchImporter.DeriveInventory` accepts only part-of-speech labels (`noun`, `verb`, `adj`, `adv`; tags = label);
  any other label marks the batch `Invalid`. The `derive-senses` command is retired.
- `audit-export`, `audit-adjudicate`, `audit-merge`, `audit-report` and `validate-pos` run the blind
  two-judge part-of-speech audit (judge C for disagreements; `PosAudit`, `PosAuditReport`); `apply-relabel`
  (`Relabeler`) applies it once: undecided and noise rows are dropped, a uniformly wrongly tagged sense is retagged,
  a mixed sense is split per part of speech when each part has at least 14 anchors and 8 holdout rows (else
  its rows are dropped and the sense excluded). Decisions and dropped rows are written under `corpus/audit/`.
- Real-text restoration: senses that `apply-relabel` had excluded for lack of sentences are restored from real
  text. `tools/real-text/harvest.py` (Python standard library; raw downloads in the git-ignored
  `tools/real-text/cache/`) extracts candidate sentences of at most 35 words from Tatoeba (CC0 or CC BY 2.0 FR with
  contributor), allow-listed public-domain Project Gutenberg works (header and footer stripped) and open-licensed
  Wikipedia and UD sentences (licence and URL recorded); the few single web excerpts that serve as a last resort
  are entered by hand (see `tools/real-text/README.md`). `RealJudge`
  runs the blind two-judge sense judgement against a per-lemma sense menu (judge C for disagreement; `none` when no
  sense fits); `RealImport` keeps the sentences whose agreed sense matches the target, splits anchors and holdout
  by work or contributor where possible and writes `corpus/{anchors,holdout}/real-restore.tsv`,
  `corpus/senses/real-restore.tsv` and the provenance sidecar `corpus/provenance/*.tsv`. `validate-provenance`
  (`RealImport.Validate`) checks that every restored and real-text sentence has a provenance row that matches its
  lemma, sense and split; that the licence and origin (`real`, `authored` or `llm-corpus`) are from the allowed
  lists; that a CC BY 2.0 FR row names a contributor, a `web-excerpt` row has a URL, and a Gutenberg row has a
  `pg` key and the Public domain licence; and that no sentence has more than 35 words or mentions Gutenberg.
  `RealImport.Import` reads `relabel-decisions.tsv` before writing any output, so a missing file leaves the corpus
  unchanged, and it marks every `drop` or `exclude` decision of a plan lemma as `restore` (not only the senses that
  actually received sentences); the plan defines the restored lemmas.
- `real-eval-import` and `real-eval` (`RealEval`) keep randomly sampled, blind-judged real sentences in
  `corpus/realtext/` (never used for training) and measure the table on them against the LLM-written holdout.
- After the sense evaluation the report lists group-level accuracy (the sense's `group`) and tag-level
  accuracy on the holdout: noun versus verb (best
  tag, answered accuracy, coverage; rows of lemmas with both tags and per gold tag; mixed and untagged rows
  are excluded and counted) and has-tag accuracy per tag.
- The embedded form map contains only forms whose lemma has a classifier, so every form resolves to a
  lemma that exists in the table; forms of lemmas without a classifier are omitted.

### Analysis Commands

The analysis commands (`LearningCurve`, `ErrorDiagnosis`, `WeakLemmas`, loading through `AnalysisData`) write
tab-separated files and a markdown report under `corpus/audit/analysis/`. They encode every sentence once with the
real model (as `build` does), center the features with the mean of all anchors and normalize them, so a dot product
is a cosine similarity. Three evaluation sets are used: `holdout`, `real` (holdout rows whose provenance origin is
`real`) and `real-eval` (`corpus/realtext/eval-seed.tsv`). Rows of a lemma without anchors or with an unknown sense
are skipped.

- `learning-curve` trains each lemma on k = 2, 4, 7, 10, 14 anchors per sense (capped at what a sense has) and on all
  anchors, using `TableBuilder.Fit` with the same settings as `build`. Each k uses several subsets (default 5). A
  subset is a partial Fisher-Yates draw per sense driven by a SplitMix32 generator seeded from the FNV-1a hash of the
  UTF-8 bytes of lemma, k and subset index, combined with `--seed`; `string.GetHashCode` is never used, so the
  output is byte-identical on repeat runs. The abstain margin is the one chosen for the full anchor set. Per lemma and
  set it reports best-sense accuracy (mean, min, max over subsets), the answered rate and the answered accuracy; the
  summary aggregates macro (mean of lemmas) and micro (pooled rows). The plateau estimate is the slope of accuracy per
  added anchor per sense between the last point and the latest earlier point at least 3 anchors per sense away,
  extrapolated linearly to twice the anchors and limited to the remaining error; a slope below 0.002 per anchor is
  plateaued.
- `error-diagnosis` examines each misclassified or abstained row. `s_same` and `s_other` are the cosine similarities
  to the nearest anchor of the labeled sense and of any other sense of the lemma. The row is a `coverage-gap` when
  both are below `FarSimilarity` (0.77, about the 10th percentile of `s_same` over correct rows); an `overlap` when both
  reach it and `s_other` is at least `s_same` minus `OverlapBand` (0.05); otherwise `ambiguous` (label-noise suspect).
- `weak-lemmas` reads those files without a model. A lemma is weak when holdout accuracy is below 95 % or, with at
  least 3 real rows, real accuracy is below 90 %. Lemmas are ranked by the mean of the holdout and real error rates.
  The recommendation follows the dominant class (ties: gap, overlap, ambiguous): `more-sentences`;
  `stronger-encoder` for overlaps on a plateaued curve, otherwise `consumer-rule`; `fix-labels`. The expected gain is
  the learning-curve gain at twice the anchors limited by the gap share, or the class share (an upper bound).

## Error Handling

- `Program.Run` returns 0 on success and 1 on a usage or data error; it never lets such an error escape. Usage
  errors (no command, unknown command, a missing or malformed option, a non-numeric integer option) and data
  errors (`InvalidDataException`, `ArgumentException`, `IOException` including a missing file, and
  `UnauthorizedAccessException`) are written to the error writer as `Error: <message>`.
- `import-batches` reports row- and lemma-level problems per batch and only exits 1 when a batch is `Invalid` or
  missing; an invalid batch imports nothing.
- The analysis commands need their inputs: a missing corpus, model or analysis file is an `IOException` and exits 1; a
  `--subsets` below 1 is an `ArgumentException`. `KeyNotFoundException` and `IndexOutOfRangeException` are deliberately
  not caught, so an internal inconsistency is not hidden.
- `validate-provenance` writes one line per problem to the error writer and exits 1 when there is any problem,
  otherwise 0.
- Readers of tab-separated files (`rows.tsv`, judge input and answer files, `dropped-rows.tsv`, the candidate and
  judgement files, `relabel-decisions.tsv`, the restore plan, provenance files) reject a row with an unacceptable
  number of columns with an `InvalidDataException` that names the file and line. The accepted ranges are: judge
  answers 3 or 4 columns, judge input at least 3, `dropped-rows.tsv` at least 6, candidates exactly 9, judgements
  exactly 10 and `relabel-decisions.tsv` exactly 9.

## Key Methods and Callers

`Program.Run` dispatches each command to `TableBuilder`, `BatchImporter`, `PosAudit`, `Relabeler`, `RealPlan`,
`RealJudge`, `RealImport`, `RealEval`, `LearningCurve`, `ErrorDiagnosis` or `WeakLemmas`. It is called by `Main`
and by the builder tests; the tool is run by hand and by the corpus maintenance workflow, never by the library or
the CI build.

## Interactions

Uses `WordPieceTokenizer`, `OnnxWordSenseEncoder`, `WordSenseTable` and `FeatureExtractor` from the
library through `InternalsVisibleTo`.
