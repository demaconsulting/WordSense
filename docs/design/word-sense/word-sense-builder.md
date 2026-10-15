# WordSenseBuilder Unit Design

## Overview

`tools/WordSense.Builder` is a non-shipped console tool with these commands:
`build --model DIR --corpus DIR --output FILE [--report FILE]`,
`import-batches --source DIR --corpus DIR [--max N]`, the audit commands (`audit-export`, `audit-adjudicate`,
`audit-merge`, `audit-report`, `validate-pos`), `apply-relabel` and the real-text commands (`real-gap`,
`real-judge-export`, `real-judge-adjudicate`, `real-judge-merge`, `real-import`, `real-eval-import`, `real-eval`,
`validate-provenance`).

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
  `adj`, `adv`; qualifiers `past`, `present`, `comparative`, `superlative` allowed). Labels of
  `corpus/forms/*.tsv` must be senses of a trained lemma; a forms label without a sense is
  an error, and forms of untrained lemmas are ignored.
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
  contributor), allow-listed public-domain Project Gutenberg works (header and footer stripped), open-licensed
  Wikipedia and UD sentences (licence and URL recorded) and, as a last resort, single web excerpts. `RealJudge`
  runs the blind two-judge sense judgement against a per-lemma sense menu (judge C for disagreement; `none` when no
  sense fits); `RealImport` keeps the sentences whose agreed sense matches the target, splits anchors and holdout
  by work or contributor where possible and writes `corpus/{anchors,holdout}/real-restore.tsv`,
  `corpus/senses/real-restore.tsv` and the provenance sidecar `corpus/provenance/*.tsv`. `validate-provenance`
  (`RealImport.Validate`) requires a provenance row (hash id, source, allowed licence, URL, contributor or work,
  origin `real`, `authored` or `llm-corpus`) for every real sentence, at most 35 words and no Gutenberg text.
- `real-eval-import` and `real-eval` (`RealEval`) keep randomly sampled, blind-judged real sentences in
  `corpus/realtext/` (never used for training) and measure the table on them against the LLM-written holdout.
- After the sense evaluation the report lists group-level accuracy (the sense's `group`) and tag-level
  accuracy on the holdout: noun versus verb (best
  tag, answered accuracy, coverage; rows of lemmas with both tags and per gold tag; mixed and untagged rows
  are excluded and counted) and has-tag accuracy per tag.
- The embedded form map contains only forms whose lemma has a classifier, so every form resolves to a
  lemma that exists in the table; forms of lemmas without a classifier are omitted.

## Interactions

Uses `WordPieceTokenizer`, `OnnxWordSenseEncoder`, `WordSenseTable` and `FeatureExtractor` from the
library through `InternalsVisibleTo`.
