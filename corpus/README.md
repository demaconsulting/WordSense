# WordSense Corpus

Training and evaluation data of the encoder models, and the inputs of the word table. The sentences identify the fine
sense of a word in context: an Open English WordNet (OEWN) sense with a readable id such as `back#spine`.

> **Current state.** The sentence folders `anchors/`, `holdout/` and `provenance/` hold the first reviewed batch of 20
> lemmas (`back`, `even`, `first`, `forward`, `go`, `help`, `home`, `however`, `just`, `little`, `long`, `only`, `own`,
> `please`, `pretty`, `review`, `right`, `still`, `test` and `well`; 294 senses, 3,488 anchors and 984 held-out
> sentences) and the relabeled legacy corpus (`relabel-*.tsv`: 21,552 anchors, 11,385 held-out sentences and, in
> `realtext/`, 655 real-text sentences), all in the fine-sense row format below. The former class-labeled corpus
> moved to `legacy/`; the Basic model still trains from it (see `legacy/README.md`). The rest of this page, from "Sense
> Files and Forms File" on, describes that legacy data and the history of how it was built; the builder commands it
> names (for example `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`,
> `error-diagnosis`, `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record.

## Layout

<!-- markdownlint-disable MD013 -->

| Folder | File pattern | Purpose |
| ------ | ------------ | ------- |
| `anchors/` | `*.tsv` | Labeled training sentences (fine senses) |
| `holdout/` | `*.tsv` | Labeled evaluation sentences, never used for training or for choosing thresholds |
| `choosers/anchors/`, `choosers/holdout/` | `*.tsv` | Training and evaluation rows of the form choosers (the lemma or tense of a shared form) |
| `realtext/` | `*.tsv` | Relabeled real-text sentences for evaluation only (never trained on; no evaluator reads them yet) |
| `provenance/` | `*.tsv` | Origin of every sentence: writer, selector, license |
| `disputed/` | `*.tsv` | Legacy rows the relabeling could not place (unsure, duplicate, no analysis, capitalized lemma); never trained |
| `coverage/` | `*.tsv`, `README.md` | The quota report of every sense (anchors and held-out sentences against the tiers) |
| `legacy/` | see `legacy/README.md` | The former class-labeled corpus (Basic still trains from it) and the CC BY-SA rows that the relabeling excluded |
| `curation/` | `*.tsv`, `README.md` | The reviewed senses and groups (the input of `seed-inventory`) |
| `lexicon/` | `*.tsv` | The dictionary word list, the auxiliaries and the form rules (inputs of `compile-image`) |
| `ud/` | `*.tsv` | Tag statistics of the UD English-EWT treebank for the Basic model (see `ud/PROVENANCE.md`) |
| `migration/` | `*.tsv` | The temporary map from the retired classes to fine senses, used to relabel the legacy corpus |
| `audit/` | `*.tsv`, `*.md` | Records of earlier audits |

<!-- markdownlint-enable MD013 -->

Every file is UTF-8 without a byte-order mark, with LF line endings.

## Sentence Files

Rows have four tab-separated columns and no header: `lemma`, `sense_id`, `part_of_speech` and `sentence`.

- `sense_id` is the public id of a sense of the word image (`lemma#slug`; project senses such as `right#discourse-marker`
  have one too). It must exist in the sense catalog and belong to `lemma`.
- `part_of_speech` is the name of the part of speech of that sense (`Noun`, `Verb`, `Adjective`, `Adverb`, ...). The builder
  rejects a row whose part of speech is not the one of its sense.
- The sentence marks the target word by wrapping it in exactly one pair of braces, for example
  `The {back} of the chair was cracked.` The braced word, looked up in lower case, must have an analysis with the lemma
  and the part of speech of the row.

The builder validates the files when it trains (`weights`), evaluates (`evaluate`), writes the appendix or reports
coverage: a malformed row is an error, and so is a sentence that occurs twice or in both `anchors/` and `holdout/`. A
lemma needs anchors for at least two of its senses to get weights; a sense with a single anchor still takes part in the
training.

`anchors/batch-1.tsv` and `holdout/batch-1.tsv` are the rows of the first reviewed batch: the sentences were written
by a language model (`gpt-5.6-luna`), filtered by a blind judge and selected by `claude-sonnet-5.5`, with 20, 10 or 5
anchors and 5, 3 or 2 held-out sentences per sense according to the WordNet 3.0 tag count of the sense (the tiers). The
held-out sentences come from the same generator as the anchors, so accuracy measured on them is optimistic.
`provenance/batch-1.tsv` has one row per sentence (`id`, `lemma`, `sense`, `split`, `source`, `licence`, `source_key`,
`url`, `contributor`, `origin`); the sentences are project-authored (`MIT (project-authored)`).

## Relabeled Legacy Corpus

`anchors/relabel-anchors.tsv`, `holdout/relabel-holdout.tsv` and `realtext/relabel-realtext.tsv` are the sentences of
the former class-labeled corpus (`legacy/`) relabeled to fine senses; `provenance/relabel.tsv` has one row per sentence
with the original source, licence, source key, URL and contributor, and a note in the contributor column that records
how the row was relabeled (`relabeled by rule from legacy class lemma#class` or `relabeled by gpt-6.1-sol from legacy
class lemma#class`). The split of a sentence (anchors, holdout or real text) is the one of the legacy row, so the
anchors and the holdout stay disjoint. How the rows were made:

1. **Licence filter.** Rows whose source is Wikipedia, Wiktionary or the UD English treebank (CC BY-SA 4.0) are not
   relabeled and never train: they were copied to `legacy/excluded-cc-by-sa/`. Tatoeba rows (CC BY 2.0 FR, CC0 1.0),
   Project Gutenberg
   rows (public domain) and the project-authored rows stay with their recorded attribution; the legacy rows without a
   provenance record are LLM-written (`MIT (project-authored)`).
2. **By rule.** A row whose legacy class maps to exactly one fine sense (`migration/legacy-class-map.tsv`, with the
   generated labels of the map translated to the curated ones) is relabeled to that sense when the lexicon analyzes the
   braced word as the lemma and part of speech of the sense.
3. **Judged.** Every other row is labeled by one judge (`gpt-6.1-sol`, chosen by the calibration on the 984 held-out
   sentences of `batch-1`, see `docs/build_notes/relabel-results.md`) that sees the sentence, the target and the
   full sense list (id, part of speech, gloss) of every lemma the target word can be a form of, and answers one sense
   id or `unsure`. Rows the judge was unsure about, rows whose sense has no analysis of the word, rows of a
   capitalized lemma (the corpus format has lower-case lemmas) and sentences that occur twice are not written: they are
   listed with the reason in `disputed/relabel-disputed.tsv` for later review.

Every `relabel-*` row validates like any other row of its folder, so the weights and the evaluation read them without
change.

## Chooser Files

`choosers/anchors/*.tsv` and `choosers/holdout/*.tsv` train and measure the form choosers: the small classifiers that
decide which lemma, or which tense of one lemma, a word form has (`left` as a form of `leave` or of the adjective
`left`, `read` as a past or a present verb). Rows have three tab-separated columns and no header: `form`, `label` and
`sentence`.

- `form` is the lower-case word form; the braced word of the sentence, lower-cased, must be the form, and the form must
  be in the word image.
- `label` names what the sentence shows: the lemma of the analyses it stands for (`leave`), or, for a lemma whose
  analyses of the form differ in features (the past and the present of `read`), one analysis written
  `lemma/features` (`read/Tense=Past+VerbForm=Part`). The other analyses of such a lemma (the noun `read`) share the
  label `read`. A label must stand for an analysis of the form (the table checks the same rule when the weights load).
- The sentence marks the form in exactly one pair of braces, as in the fine-sense files.

The rows are the same sentences as the fine-sense rows, labeled a second way, and keep their split: a held-out sentence
never trains a chooser. A sentence occurs once across both chooser sets. `relabel-anchors.tsv` and `relabel-holdout.tsv`
hold the rows made from the relabeled legacy corpus: the forms of several lemmas (`used`, `left`, `bases`, `axes`,
`analyses`, `diagnoses`, `ellipses`, `rows`, the participles that are adjectives) take the lemma of the sense the judge
chose, and
the verb forms with two tenses (`read`, `reread`, `cast`, `upset`, ...) take the tense of the legacy class (`read`,
`reread`) or the one a second judge call chose. The builder trains a chooser for a form when its rows name at least two
labels and every analysis of the form has a label among them, and reports the forms that got none (the quota filling
adds the missing lemmas).

## Quota-Filled Rows (targeted)

`anchors/`, `holdout/`, `choosers/` and `provenance/` also hold the files `quota-w<N>-real.tsv`, `quota-w<N>-written.tsv`
and `quota-w<N>-forms.tsv` (chooser rows of a form), made by the quota filling (`tools/quota-fill`, see
`docs/build_notes/quota-fill.md`): they bring every sense to the quota of its tier (see `coverage/README.md`).

**Every quota-filled row is targeted.** It was found or written for one specific sense (or, for the chooser files, one
specific form), never drawn at random from a text. Its provenance row says so in three extra columns after `origin`:
`selection` (`targeted`), `selection_reason` (`sense=<sense id>`, or `form=<form>` for a chooser row) and `found_by`
(how it was found: `harvest: pool sentence with the form '...' of the lemma, found in <document>; labeled gpt-6.1-sol
(high confidence, full sense list of the form)`, or `written: gpt-5.6-luna wrote it for the sense; gpt-6.1-sol labeled it
blind with the same sense`). The real sentences come only from public-domain or CC0 text (Project Gutenberg books
published 1930 or earlier, Tatoeba sentences of the CC0 export, VOA Learning English staff-written articles and the
USGS, NPS and National Weather Service pages, whose agencies state that their material is in the public domain); the
licence column names the basis and the contributor column the document.

**The encoder corpus is a balanced classifier corpus.** `anchors/`, `holdout/` and `choosers/` (all of them, not only
the quota-filled files) hold the number of sentences that the quota of a sense asks for, not a sample of running text:
the share of a sense in them says nothing about how often the sense occurs. They must **never** be used for usage
statistics (tagger or sense counts, frequency priors, word frequencies). Held-out sentences found for a sense are fine
for measuring the classifier, but a figure measured on them is not the accuracy on representative real text:
`evaluate` reports the held-out rows by origin (targeted real text, earlier harvest, LLM-written) and says so. The
representative measurements are the open English part-of-speech corpus and SemCor (the independent evaluation).

## Sense Files and Forms File (legacy)

The following sections describe the retired class-labeled files that are now in `legacy/`; the paths in them are
relative to `legacy/`. Legacy sentence rows have three columns (`lemma`, `sense` label, `sentence`; the sense label is
described by the inventory below). Sources are grouped by file name.
`seed-34.tsv` is the 34-lemma seed set; `speech-batchN.tsv` files are imported by the
builder's `import-batches` command. A file under `anchors/` has a matching file of the same name under `holdout/`.

## Forms File

`forms/speech.tsv` has the header `form`, `lemma`, `labels` (tab-separated). `labels` is the comma-separated list of
senses that the form can take, for example `abstracts -> abstract : noun,verb`. The builder keeps the form-to-lemma
mapping for lemmas that have a trained classifier, and a listed form answers with only the senses its labels name (in
the order of the lemma). Every label must be a sense of the trained lemma (from the inventory); an unknown, duplicate
or empty label is a build error, and so is a row for the lemma itself (form equals lemma) that does not list every
sense (omit such a row instead). A row that lists all senses reuses the lemma's senses.

Completeness rule: every braced target word in `anchors/`, `holdout/` and `realtext/` must appear in this file (or be
a trained lemma itself) and map to the lemma of its row, otherwise the classifier cannot find the lemma and the row
is skipped or unanswered in the evaluations. The guard test
`WordSense_EmbeddedTable_EveryCorpusTargetResolvesToItsLemma` checks it. The one documented exception is the plural
`rows` of the lemma `row`, since `rows` is a trained lemma of its own. After editing this file, rebuild the
embedded table.

## Batch Import Rules

`import-batches` treats a specified lemma without sentences as a warning (no classifier, or trained but not
evaluated). Rows with format errors, an unknown lemma, a disallowed label, a duplicate sentence or an
anchor/holdout overlap are dropped and reported; a batch with more than 5 % such rows is rejected. A lemma with
fewer than two anchor senses or fewer than four anchors for a represented sense, and a lemma with holdout but no
anchors, is excluded. `speech-batchN.tsv` files are copied unchanged apart from the dropped rows.

Not imported (real text too thin, see `docs/build_notes/gap-lemmas-decisions.md`): cumulate, depute, domesticate,
illegitimate, outcaste, overman, overstudies, overstudy, overwater, unstick. The gap lemmas conjunct, overhead and
uptown are stored in the `real-restore.tsv` files (real sentences only). Lemmas of batches 1-8 without holdout
sentences are trained but not evaluated (30 lemmas).

## Sense Inventory

`senses/*.tsv` has the header `lemma`, `label`, `pos`, `meaning`, `respelling` (tab-separated, five columns). A *lemma*
is the dictionary base form of a word (`record` for `records`); a *heteronym* is a lemma whose meanings are pronounced
differently (`record`, `bow`).

- `label` is an internal join key that ties the anchor sentences, the holdout sentences, the forms and the inventory
  together. It is never shipped in the table or shown to callers, so it may be any name that is unique within the
  lemma (it is often the part of speech, or a short description).
- `pos` is one of the ten `PartOfSpeech` names (the corpus uses `noun`, `verb`, `adj` and `adv`; `auxiliary` is
  not accepted, because Auxiliary senses are listed in `lexicon/auxiliaries.tsv`, never trained; the first
  letter may be upper or lower case).
- `meaning` is a lower-case identifier of at most 32 characters (letters, digits and hyphens, starting with a
  letter) that names the meaning of a heteronym, for example `weapon` and `bend` for `bow`. It may be empty.
- `respelling` is the plain respelling of the pronunciation in the library's own convention: syllables separated by
  hyphens, stress in capitals, no phonetic alphabet (`REK-urd` for the noun `record`, `rih-KORD` for the verb; `boh`
  for the weapon `bow` and `bow` for the bend). It may be empty, and it needs a meaning.

The inventory must list exactly the senses used by the anchors: an anchor label missing from the inventory, or an
inventory sense without anchors, fails the build.

Inventory rules (checked by every build): every sense has a defined part of speech; meanings are
all or none within a lemma; a lemma with meanings (a heteronym) has at least two distinct meanings; a
(part of speech, meaning) pair appears once per lemma, so two senses of one part of speech are allowed only with
different meanings; one meaning always has the same respelling; distinct meanings have distinct respellings. A lemma
without meanings has no pronunciation in the answers. The earlier tag and group columns and the `derive-senses`
rules are retired.

## Real Sentences and Provenance

`anchors/real-restore.tsv`, `holdout/real-restore.tsv` and `senses/real-restore.tsv` restore the senses that were
excluded for lack of sentences (`bow`, `lead`, `tear`, `closer`, `upsetting`, `polish`, `orient` and others) with
real sentences. Each real sentence has a row in `provenance/*.tsv` (`id` = first 12 hex of SHA-256 of
`lemma|sentence`, `lemma`, `sense`, `split`, `source`, `licence`, `source_key`, `url`, `contributor`, `origin`).
Allowed licences: `CC0 1.0`, `Public domain`, `CC BY 2.0 FR`, `CC BY-SA 4.0`, `MIT (project-authored)` and, as a last
resort, `web-excerpt`. `realtext/eval-seed.tsv` holds real sentences for evaluation only (never trained on). The
builder command `validate-provenance` checks the sidecar. The corpus is not shipped in the NuGet package.
`anchors/real-gap-1.tsv`, `holdout/real-gap-1.tsv` and `provenance/real-gap-1.tsv` (Phase 3b, plan in
`audit/real-gap-1-plan.tsv`, judged rows in `audit/real-gap-audit-1.tsv`) add real anchors and a real holdout for the
weak lemmas `conjugate`, `insert` and `present`; the holdout rows are never anchors. See
`docs/build_notes/weak-lemma-decisions.md`.
`tools/real-text/README.md` describes the repeatable harvest. Senses that real text cannot fill are completed by
`anchors|holdout|provenance/new-authored-N.tsv` (command `real-authored-import`; two independent authors, origin
`authored`, contributor `authored-anchor` or `authored-holdout`). `audit/new-lemma-speech-map.tsv` is a historical
record of the Phase 4 lemmas (the pronunciation class and an authoring-time note of each sense, with its counts,
source mix (real or authored) and holdout accuracy); the shipped respellings live in the `respelling` column of the
inventory; see `docs/build_notes/new-lemma-results.md`. Phase 5 noun/verb lemmas are imported by `nounverb-import`
into
`anchors|holdout|provenance|senses/nounverb-N.tsv` (origins real or `user-supplied`, licence
`MIT (project-authored)` for the latter) and `audit/nounverb-untrained.tsv` lists every lemma left untrained with the
reason. `audit/nounverb-dropped.tsv` lists lemmas removed by `nounverb-gate` (see
`docs/build_notes/nounverb-results.md`).

## Analysis Output

`audit/analysis/` holds the output of `learning-curve`, `error-diagnosis` and `weak-lemmas` (accuracy against anchors
per sense, the diagnosed errors and the ranked weak lemmas); see `docs/build_notes/learning-curve-and-diagnosis.md`.

## Part-of-Speech Audit (historical)

`audit/pos-audit.tsv` holds the blind two-judge audit of every anchor and holdout sentence (judge C for
disagreements); `audit/judge-prompt.md` is the judge prompt. The audit was applied once: it retagged, split or
dropped rows and excluded senses, recording the decisions in `audit/relabel-decisions.tsv` and the dropped rows in
`audit/dropped-rows.tsv`. These files are a record; the commands that produced them (`audit-*`, `apply-relabel`) are
retired, and `real-import` still reads `relabel-decisions.tsv`. See `docs/build_notes/pos-audit.md`.

## Label Reconciliation

The sense labels of a lemma come from its anchor sentences. The labels of a listed form are authoritative for that
form: every anchor, holdout and real-text sentence whose braced form is listed must carry a sense that the labels
name, otherwise the build fails. Where they disagree, widen the labels of the form (the forms file) rather than
dropping the sentence.
