# WordSense Corpus

Training and evaluation data for the per-lemma sense classifiers.

## Layout

| Folder | File pattern | Purpose |
| ------ | ------------ | ------- |
| `anchors/` | `*.tsv` | Labeled training sentences |
| `holdout/` | `*.tsv` | Labeled evaluation sentences, never used for training |
| `forms/` | `*.tsv` | Inflected form to lemma map |

Every file is UTF-8 without a byte-order mark, with LF line endings.

## Sentence Files

Rows have three tab-separated columns and no header: `lemma`, `sense`, `sentence`. The sentence marks the target word
by wrapping it in exactly one pair of braces, for example `The {record} for the fastest mile was set in 1999.`

Sources are grouped by file name. `seed-34.tsv` is the 34-lemma seed set; `speech-batchN.tsv` files are imported by the
builder's `import-batches` command. A file under `anchors/` has a matching file of the same name under `holdout/`.

## Forms File

`forms/speech.tsv` has the header `form`, `lemma`, `labels` (tab-separated). `labels` is the comma-separated list of
senses that the form can take, for example `abstracts -> abstract : noun,verb`. The builder keeps only the
form-to-lemma mapping for lemmas that have a trained classifier. Every label must be a sense of the trained lemma
(from the inventory); a label without a sense is a build error.

## Batch Import Rules

`import-batches` treats a specified lemma without sentences as a warning (no classifier, or trained but not
evaluated). Rows with format errors, an unknown lemma, a disallowed label, a duplicate sentence or an
anchor/holdout overlap are dropped and reported; a batch with more than 5 % such rows is rejected. A lemma with
fewer than two anchor senses or fewer than four anchors for a represented sense, and a lemma with holdout but no
anchors, is excluded. `speech-batchN.tsv` files are copied unchanged apart from the dropped rows.

Not imported (no anchors, so no classifier): conjunct, cumulate, depute, domesticate, illegitimate, outcaste,
overhead, overman, overstudies, overstudy, overwater, unstick, uptown. Lemmas of batches 1-8 without holdout
sentences are trained but not evaluated (30 lemmas).

## Sense Inventory

`senses/*.tsv` has the header `lemma`, `sense`, `tags`, `group` (tab-separated; a legacy 3-column file is still
read). `tags` is a `|`-separated, ordinal-sorted list. The optional `group` (default: the sense id) names the
coarser group a split sense belongs to, exposed as `WordSense.Group`. The inventory must list exactly the senses
used by the anchors: an anchor label missing from the inventory, or an inventory sense without anchors, fails the
build.

Tag rule: tags are true parts of speech (`noun`, `verb`, `adj`, `adv`) plus qualifiers (`past`, `present`,
`comparative`, `superlative`); a sense has exactly one part of speech. A tag never stands in for a pronunciation or
meaning. The build fails on violations (`PosRules`). The earlier `derive-senses` rules are retired.

## Part-of-Speech Audit

`audit/pos-audit.tsv` holds the blind two-judge audit of every anchor and holdout sentence (judge C for
disagreements); `audit/judge-prompt.md` is the judge prompt. `apply-relabel` applied the audit once: it retagged,
split or dropped rows and excluded senses, recording the decisions in `audit/relabel-decisions.tsv` and the dropped
rows in `audit/dropped-rows.tsv`. See `docs/build_notes/pos-audit.md`.

## Label Reconciliation

The sense labels of a lemma come from its anchor sentences. Where the forms file disagrees (for example a form whose
label list contains a sense the anchors lack), the forms file is normalized rather than the anchors.
