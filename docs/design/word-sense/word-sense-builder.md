# WordSenseBuilder Unit Design

## Overview

`tools/WordSense.Builder` is a non-shipped console tool with these commands:
`build --model DIR --corpus DIR --output FILE [--report FILE]`,
`import-batches --source DIR --corpus DIR [--max N]`, the audit commands (`audit-export`, `audit-adjudicate`,
`audit-merge`, `audit-report`, `validate-pos`), `apply-relabel` and the real-text commands (`real-gap`,
`real-judge-export`, `real-judge-adjudicate`, `real-judge-merge`, `real-import`, `real-eval-import`, `real-eval`,
`validate-provenance`) and the analysis commands (`learning-curve --corpus DIR --model DIR --out DIR [--subsets N]
[--seed N] [--lemmas a,b]`, `error-diagnosis --corpus DIR --model DIR --out DIR [--lemmas a,b]` and
`weak-lemmas --analysis DIR [--report FILE]`) and the documentation command
`generate-appendix --corpus DIR --output FILE`.

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
  `pg` key and the Public domain licence; that an `authored` row carries the `MIT (project-authored)` licence; and
  that no sentence has more than 35 words or mentions Gutenberg. The checked sentence files are `real-*.tsv` and
  the new-lemma batch files `new-*.tsv` of `anchors` and `holdout`, plus `realtext/*.tsv`. A sense is one part of
  speech; two senses of the same part of speech (for example a noun and an agent noun with different
  pronunciations) are valid and are told apart by their `group`.
  `RealImport.Import` reads `relabel-decisions.tsv` before writing any output, so a missing file leaves the corpus
  unchanged, and it marks every `drop` or `exclude` decision of a plan lemma as `restore` (not only the senses that
  actually received sentences); the plan defines the restored lemmas.
- `real-gap-import` (`RealGap`) adds real sentences for senses of existing lemmas (Phase 3b): the plan file
  `corpus/audit/real-gap-N-plan.tsv` (`lemma`, `sense`, `definition`, `add_anchors`, `real_holdout`) gives the sense
  menus (`real-judge-export --gap-plan`, frozen in `menus.tsv` of the work directory), the sentences to add as
  anchors and the real holdout size per sense (the existing real holdout of other files counts). `RealImport.Allocate`
  fills the holdout first from small contributors and takes the anchors from the others, so the sets are disjoint;
  output is `corpus/{anchors,holdout,provenance}/real-gap-N.tsv`, and a sense short of rows is reported. `real-eval`
  scores these holdout rows as the sets `real-gap` and `llm-holdout-gap-lemmas`.
- `real-authored-import` (`RealAuthored`) completes senses that real text cannot fill (new lemmas, Phase 4):
  `--corpus DIR --name new-authored-N --anchors FILE --holdout FILE`, where the two files
  (`lemma<TAB>sense<TAB>sentence`) come from two independent authors who never see each other's output (frozen prompts in
  `tools/real-text/authoring-prompt.md`). Every problem is collected and nothing is written on error: a sense not in the
  restore plan, a target that is not exactly one braced word, a target that is not a form of the lemma in
  `corpus/forms/*.tsv`, a sentence of more than 35 words, and a sentence (case-insensitive) that repeats inside a file,
  across anchors and holdout, or an existing row of any other file. Output is
  `corpus/{anchors,holdout,provenance}/new-authored-N.tsv` with origin `authored`, licence `MIT (project-authored)` and
  contributor `authored-anchor` or `authored-holdout`, rows in ordinal order, plus `corpus/senses/new-authored-N.tsv`
  only for planned senses that no other file declares (a sense is declared once). The report lists per sense the
  authored and total counts against the minima 14 and 8. Order rule: `RealPlan.ReadExisting` ignores
  `real-restore.tsv` and every `new-authored-*.tsv`, so authored rows never shrink the number of real rows that
  `real-import` takes and a later `real-import` is not affected by them; if a rerun of `real-import` starts to declare
  a sense that an authored batch declared, move the `senses/new-authored-N.tsv` file away and import the batch again.
- `nounverb-import` (`NounVerbImport`) trains noun/verb ambiguous lemmas from merged real sentences (Phase 5):
  `--corpus DIR --name nounverb-N --sentences F1[,F2..] --provenance P1[,P2..] --exclusions FILE [--forms FILE]
  [--min-anchors N --min-holdout N --max-anchors N --max-holdout N]`. A sentence file has
  `lemma<TAB>noun|verb<TAB>sentence` with one braced target; provenance files carry the columns
  `sentence_hash lemma sense source licence url contributor_or_work` (matched on `PosAudit.RowId`). Rows are dropped
  and counted by reason: excluded lemma (`tools/real-text/nounverb-exclusions.tsv`: names, adjective-heavy, spurious
  part of speech, collision), lemma already in the inventory, missing or disagreeing provenance, more than 35 words,
  a licence outside the allowed list, a repeat of an existing corpus sentence or of another input row, and a target
  that is not a form of the lemma or is a collision form. Licences are normalized (`Public domain (US)` to
  `Public domain`, `CC0` to `CC0 1.0`; the origin `user-supplied` gets `MIT (project-authored)`). A lemma is trained
  only with at least 14 + 8 rows for both noun and verb. Each sense is split by source group (Tatoeba contributor, UD
  document, Gutenberg work, Wiktionary page): the holdout (30 %, between 8 and 12 rows) takes whole small groups
  first and the anchors (at most 40) are taken round-robin from the other groups; rows of a holdout group serve as
  anchors only when the minimum cannot otherwise be met, and are reported as shared. The order inside a group is by a
  hash of the row id, so the output is deterministic. Inflected forms (`-s/-es/-ies`, `-ed/-ing` with e-drop,
  doubling, `c` to `ck`; irregular forms from `--forms`) are written to `corpus/forms/speech.tsv`; a form that is the
  base of another new lemma, a trained lemma, claimed by two new lemmas or already a form of another lemma is left
  out and reported (for example `lie`, `bit`, `found`). Output is
  `corpus/{anchors,holdout,provenance,senses}/nounverb-N.tsv` and `corpus/audit/nounverb-untrained.tsv` (lemma, noun
  and verb counts, reason: `dominant-use`, `insufficient-data`, `excluded`, `already-trained`). Everything is
  computed before anything is written, and a rerun with the same inputs replaces the previous output of the same
  name byte for byte. `RealPlan.ReadExisting` ignores `nounverb-*.tsv`, and `validate-provenance` checks them with
  the extra origin `user-supplied`.
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

### Appendix Generation

`generate-appendix` (`AppendixGenerator`) writes `docs/user_guide/appendix-senses.md` from `corpus/senses`,
`corpus/forms` and `corpus/anchors`; it needs no model. The document is deterministic: lemmas and senses are in ordinal
order, forms are listed once per lemma (excluding the lemma itself), the example of a sense is the shortest anchor
sentence of at least 30 characters (ties ordinal; the longest anchor when none is that long) with its target in bold,
markdown-significant characters of the sentence are escaped, text is wrapped at 120 characters (the markdown
line-length limit) without breaking before a word that would read as a list or heading marker, lines end with LF and
the file ends with one line feed. The explanatory part states the tag set (`PosRules.PartsOfSpeech` and
`PosRules.Qualifiers`, with the number of senses carrying each tag), groups and the form map. The input is checked:
a missing `senses`, `forms` or `anchors` directory, an inventory without senses, a sense without an anchor sentence
and a form label that is not a sense of the lemma are reported together as an `InvalidDataException`. Forms of
lemmas that have no sense are ignored, as in `build`. The appendix is generated, never edited by hand; a test and
`lint.ps1` regenerate it to a temporary file and fail when the committed file differs. The generated file is excluded
from spell checking (`.cspell.yaml`) because the corpus contains many rare words and names; the line-length rule
still applies to it.

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
`RealJudge`, `RealImport`, `RealEval`, `LearningCurve`, `ErrorDiagnosis`, `WeakLemmas` or `AppendixGenerator`. It is
called by `Main` and by the builder tests; the tool is run by hand and by the corpus maintenance workflow, never by
the library or the CI build.

## Interactions

Uses `WordPieceTokenizer`, `OnnxWordSenseEncoder`, `WordSenseTable` and `FeatureExtractor` from the
library through `InternalsVisibleTo`.
