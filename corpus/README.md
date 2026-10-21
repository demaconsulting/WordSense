# WordSense Corpus

Training and evaluation data of the encoder models and the cue tables of the Basic model, the inputs of the word table,
and the open English part-of-speech corpus (`open/`). The sentences identify the fine sense of a word in context: an Open
English WordNet (OEWN) sense with a readable id such as `back#spine`.

<!-- markdownlint-disable MD013 -->

## License of the Corpus

**Every sentence in the training inputs is CC0 1.0, public domain in the United States, or project-authored.** The rows
written by the project (including the ones written by language models for the project) are dedicated to the public
domain under CC0 1.0 by the project owner; their provenance licence is `CC0 1.0 (project-authored)`. The real sentences
come only from text that is public domain in the United States (Project Gutenberg books published 1930 or earlier; works
of US federal employees under 17 U.S.C. §105) or released under CC0 1.0 (the Tatoeba CC0 export). Our annotations and
selection are CC0 1.0 too. No Wikipedia, Wiktionary or Universal Dependencies (UD) text, and no CC BY or CC BY-SA text,
is a training input: the licence filter (`tools/quota-fill/license_filter.py`; the test `CorpusLicenseTests` re-checks
every provenance row) removed them. The UD English-EWT treebank (CC BY-SA 4.0) is used **only to evaluate**, from a
git-ignored download, and is never committed or shipped.

**Removed with the filter** (10,328 sentence rows): the 9,296 Tatoeba sentences under `CC BY 2.0 FR` (5,907 anchors,
3,080 held-out sentences and 309 real-text sentences) and their 1,032 chooser rows (693 anchor and 339 held-out
chooser rows, the second view of the same sentences). The Wikipedia, Wiktionary and UD rows of the earlier class-labeled
corpus had already been excluded when it was relabeled to fine senses; that corpus (`legacy/`), the map that relabeled it
(`migration/`), the rows it could not place (`disputed/`) and the UD tag counts (`ud/`) are deleted.

## Layout

| Folder | File pattern | Purpose |
| ------ | ------------ | ------- |
| `anchors/` | `*.tsv` | Labeled training sentences (fine senses) |
| `holdout/` | `*.tsv` | Labeled evaluation sentences, never used for training or for choosing thresholds |
| `choosers/anchors/`, `choosers/holdout/` | `*.tsv` | Training and evaluation rows of the form choosers (the lemma or tense of a shared form) |
| `realtext/` | `*.tsv` | Real-text sentences for evaluation only (never trained on) |
| `provenance/` | `*.tsv` | Origin of every sentence: writer, selector, license |
| `coverage/` | `*.tsv`, `README.md` | The quota report of every sense (anchors and held-out sentences against the tiers) |
| `curation/` | `*.tsv`, `README.md` | The reviewed senses and groups (the input of `seed-inventory`) |
| `lexicon/` | `*.tsv` | The dictionary word list, the auxiliaries and the form rules (inputs of `compile-image`) |
| `open/` | see `open/README.md` | The open English part-of-speech corpus (CC0 annotations over public-domain text): `representative/` (random; the only input of usage statistics, such as the Basic tagger counts in `open/counts/`, and the headline real-text evaluation) and `targeted/` (deliberate selections; a challenge set) |
| `audit/` | `*.tsv`, `*.md` | Records of earlier audits (identifiers and decisions, no sentences) |

Every file is UTF-8 without a byte-order mark, with LF line endings.

**Two kinds of corpus, two uses.** The encoder corpus (`anchors/`, `holdout/`, `choosers/`, `realtext/`) is *targeted by
nature*: each row was found or written for one sense or form, so the share of a sense in it says nothing about how often
the sense occurs. It trains classifiers (the encoder weights and the Basic cue tables) and never feeds a usage statistic.
Usage statistics (tag counts per word, tag transitions) come only from `open/representative/`.

## Sentence Files

Rows have four tab-separated columns and no header: `lemma`, `sense_id`, `part_of_speech` and `sentence`.

- `sense_id` is the public id of a sense of the word image (`lemma#slug`; project senses such as `right#discourse-marker`
  have one too). It must exist in the sense catalog and belong to `lemma`.
- `part_of_speech` is the name of the part of speech of that sense (`Noun`, `Verb`, `Adjective`, `Adverb`, ...). The builder
  rejects a row whose part of speech is not the one of its sense.
- The sentence marks the target word by wrapping it in exactly one pair of braces, for example
  `The {back} of the chair was cracked.` The braced word, looked up in lower case, must have an analysis with the lemma
  and the part of speech of the row.

The builder validates the files when it trains (`weights`, `train-basic`), evaluates (`evaluate`), writes the appendix or
reports coverage: a malformed row is an error, and so is a sentence that occurs twice or in both `anchors/` and
`holdout/`. A lemma needs anchors for at least two of its senses to get weights; a sense with a single anchor still takes
part in the training.

`anchors/batch-1.tsv` and `holdout/batch-1.tsv` are the rows of the first reviewed batch (20 lemmas): the sentences were
written by a language model (`gpt-5.6-luna`), filtered by a blind judge and selected by `claude-sonnet-5.5`, with 20, 10
or 5 anchors and 5, 3 or 2 held-out sentences per sense according to the WordNet 3.0 tag count of the sense (the tiers).
`provenance/batch-1.tsv` has one row per sentence (`id`, `lemma`, `sense`, `split`, `source`, `licence`, `source_key`,
`url`, `contributor`, `origin`); the sentences are project-authored (`CC0 1.0 (project-authored)`). The id is the first
12 hexadecimal digits of the SHA-256 of `lemma|sentence` (`form|sentence` or `label|sentence` for a chooser row).

The `relabel-*.tsv` files hold the sentences of the earlier class-labeled corpus that were relabeled to fine senses; their
provenance rows (`provenance/relabel.tsv`) note how each row was relabeled (by rule, or by `gpt-6.1-sol`), with the
original source, licence, source key and URL. Allowed licences of a provenance row: `CC0 1.0`, `CC0 1.0 (project-authored)`
and `Public domain in the United States`.

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
never trains a chooser. A sentence occurs once across both chooser sets. The builder trains a chooser for a form when its
rows name at least two labels and every analysis of the form has a label among them. The Basic cue tables also read the
anchor rows of the choosers (a form whose label names one analysis).

## Quota-Filled Rows (targeted)

`anchors/`, `holdout/`, `choosers/` and `provenance/` also hold the files `quota-w<N>-real.tsv`, `quota-w<N>-written.tsv`
and `quota-w<N>-forms.tsv` (chooser rows of a form), made by the quota filling (`tools/quota-fill`, see
`docs/build_notes/quota-fill.md`): they bring every sense to the quota of its tier (see `coverage/README.md`).

**Every quota-filled row is targeted.** It was found or written for one specific sense (or, for the chooser files, one
specific form), never drawn at random from a text. Its provenance row says so in three extra columns after `origin`:
`selection` (`targeted`), `selection_reason` (`sense=<sense id>`, or `form=<form>` for a chooser row) and `found_by`
(how it was found: `harvest: pool sentence with the form '...' of the lemma, found in <document>; labeled gpt-6.1-sol
(high confidence, full sense list of the form)`, or `written: gpt-5.6-luna wrote it for the sense; gpt-6.1-sol labeled it
blind with the same sense`), and a last column `licence_basis` with the licence basis of the document of the sentence.
The real sentences come only from text that is **public domain in the United States** or CC0: Project Gutenberg books
published 1930 or earlier, Tatoeba sentences of the CC0 export, and works of US federal employees, which are in the
public domain in the United States under 17 U.S.C. §105 and the policy of the agency (VOA Learning English staff-written
articles, USGS, NPS and National Weather Service pages, and from wave 2 the FAA handbooks and advisory circulars that
carry no third-party material; credited third-party passages are excluded). The licence column holds `Public domain in
the United States` (or `CC0 1.0` for Tatoeba), the `licence_basis` column the reason and the document (for an FAA
document, "US federal government work, 17 U.S.C. §105 (public domain in the United States)" with the document number,
edition and URL), and the contributor column the document.

**Wave 3 files.** `quota-w3-*` fills the lemmas of ranks 121-200 and the technical lemmas, `quota-w32-written.tsv` is the
second round for senses that stayed short (written with another prompt), and `quota-w33-real.tsv` and
`quota-w33-written.tsv` are the technical senses of `curation/tech-senses.tsv` (real FAA text, and simplified-technical-
English instructions written by `gpt-5.6-luna` and verified blind by `gpt-6.1-sol`). `quota-w<N>-forms*.tsv` are chooser
rows of a form; they follow the document roles like the fine-sense rows (an evaluation document gives held-out rows only,
every other document anchors only), so a chooser row cannot link anchors and the real held-out split of one document.

**The encoder corpus is a balanced classifier corpus.** `anchors/`, `holdout/` and `choosers/` (all of them, not only
the quota-filled files) hold the number of sentences that the quota of a sense asks for, not a sample of running text.
They must **never** be used for usage statistics (tagger or sense counts, frequency priors, word frequencies). Held-out
sentences found for a sense are fine for measuring the classifier, but a figure measured on them is not the accuracy on
representative real text.
**The real held-out sentences are split by document** (the first bit of the SHA-256 of `document|<document id>`, see
`tools/WordSense.Builder/RealSplit.cs`): the *calibration* half chooses the per-lemma thresholds of the encoder weights,
the *test* half never influences a weight or a threshold and is the headline of `evaluate`, which also reports the
calibration half, the written held-out sentences and the whole holdout as secondary or diagnostic sets. The documents of
the two halves are disjoint; from wave 2 on, the harvest also keeps the documents that supply held-out rows apart from
the documents that supply anchors (an *evaluation document* is chosen by the hash `SHA-256("evaldoc|" + document)`, one
in four; the other documents supply anchors only). The older rows share some documents between anchors and held-out
sentences; `evaluate` reports the test rows of documents without anchors apart. The representative measurements are the
open English part-of-speech corpus (`open/representative`) and SemCor (the independent evaluation).

## Real Sentences and Provenance

Each sentence has a row in `provenance/*.tsv` (columns above). The builder command `validate-provenance` checks that
every anchor, held-out and real-text sentence has a row with an allowed licence, and the test `CorpusLicenseTests` fails
on any other licence. The corpus is not shipped in the NuGet package.
`realtext/relabel-realtext.tsv` holds real sentences for evaluation only (never trained on).
`tools/real-text/README.md` describes the repeatable harvest of real sentences, and `tools/quota-fill/license_filter.py`
the licence filter. `audit/` keeps the records of earlier audits and analyses (blind two-judge audits of the part of
speech and the sense, the relabeling decisions, the learning curve and the weak lemmas); they hold identifiers and
decisions, not sentences, and the commands that produced most of them are retired (see
`docs/build_notes/pos-audit.md`, `docs/build_notes/learning-curve-and-diagnosis.md`).

## Label Reconciliation

The sense labels of a lemma come from its anchor sentences. The labels of a listed form are authoritative for that
form: every anchor, holdout and real-text sentence whose braced form is listed must carry a sense that the labels
name, otherwise the build fails. Where they disagree, widen the labels of the form (the forms file) rather than
dropping the sentence.

<!-- markdownlint-enable MD013 -->
