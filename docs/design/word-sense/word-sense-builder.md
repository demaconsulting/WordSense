# WordSenseBuilder Unit Design

## Overview

`tools/WordSense.Builder` is a non-shipped console tool with two commands:
`build --model DIR --corpus DIR --output FILE [--report FILE]`,
`derive-senses --corpus DIR` and
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
- `BatchImporter` imports a batch only if both files exist (otherwise `Missing`). Lemma-level absence
  (a specified lemma without anchors or holdout sentences) is a warning listed per batch: a lemma without
  anchors has no classifier and is left out, a lemma without holdout is trained but not evaluated.
  Row-level errors (bad format, not exactly one `{...}`, unknown lemma, disallowed label, duplicate
  sentence, anchor/holdout overlap or overlap with the existing corpus) are reported and only the
  offending rows are dropped (the holdout copy of an overlap); if more than 5 % of a batch's rows
  are dropped the batch is `Invalid` and nothing is imported. A lemma left with fewer than two senses,
  or fewer than four anchors for a represented sense, or with holdout rows but no anchors, is excluded
  from both sets and listed. Sentences are never created or edited. Importing is idempotent.
- A lemma classifier is trained on the senses its anchors represent only. The sense inventory
  (`corpus/senses/*.tsv`: `lemma`, `sense`, `tags`) must list exactly the senses that the anchors use:
  an anchor label that is not in the inventory, or an inventory sense without anchors, is a validation
  error. Labels of `corpus/forms/*.tsv` must be senses of a trained lemma; a forms label without a sense is
  an error, and forms of untrained lemmas are ignored.
- `derive-senses --corpus DIR` writes the inventory from the anchor labels with fixed rules
  (`SenseTagDerivation`): `noun`, `verb`, `adj` and `adv` map to themselves, `past` to `past|verb`,
  `present` to `present|verb`, noun-like labels (`argument`, `line`, `metal`, `music`, `fish`, `drop`,
  `injury`, `weapon`) to `noun`, `paddle` to `verb`, `guide`, `bend` and `rip` to `noun|verb`; `used|verb`
  is overridden to `past|verb`. An unmapped label is an error, so the batch importer marks such a batch
  `Invalid`. The importer writes the derived inventory for imported batches.
- After the sense evaluation the report lists tag-level accuracy on the holdout: noun versus verb (best
  tag, answered accuracy, coverage; rows of lemmas with both tags and per gold tag; mixed and untagged rows
  are excluded and counted) and has-tag accuracy per tag.
- The embedded form map contains only forms whose lemma has a classifier, so every form resolves to a
  lemma that exists in the table; forms of lemmas without a classifier are omitted.

## Interactions

Uses `WordPieceTokenizer`, `OnnxWordSenseEncoder`, `WordSenseTable` and `FeatureExtractor` from the
library through `InternalsVisibleTo`.
