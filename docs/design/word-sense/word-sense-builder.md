# WordSenseBuilder Unit Design

## Overview

`tools/WordSense.Builder` is a non-shipped console tool with two commands:
`build --model DIR --corpus DIR --output FILE [--report FILE]` and
`import-batches --source DIR --corpus DIR [--max N]`.

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
- `BatchImporter` copies a batch only if both files exist, parse, use only known lemmas and specified
  senses, and are disjoint from the corpus; otherwise it reports `Missing` or `Invalid`. Importing is
  idempotent.

## Interactions

Uses `WordPieceTokenizer`, `OnnxWordSenseEncoder`, `WordSenseTable` and `FeatureExtractor` from the
library through `InternalsVisibleTo`.
