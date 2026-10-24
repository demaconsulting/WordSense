# WordSense Corpus

Training and evaluation data of the encoder models and the cue tables of the Basic model, the inputs of the word table,
and the open English part-of-speech corpus (`open/`). The sentences identify the fine sense of a word in context: an Open
English WordNet (OEWN) sense with a readable id such as `back#spine`.

<!-- markdownlint-disable MD013 -->

## License of the Corpus

**Every sentence in the training inputs is CC0 1.0, public domain in the United States, or project-authored.** The rows
written by the project (including the ones written by language models for the project) are dedicated to the public
domain under CC0 1.0 by the project owner. The real sentences come only from text that is public domain in the United
States (Project Gutenberg books published 1930 or earlier; works of US federal employees under 17 U.S.C. §105) or
released under CC0 1.0 (the Tatoeba CC0 export). Our annotations and selection are CC0 1.0 too. No Wikipedia, Wiktionary
or Universal Dependencies (UD) text, and no CC BY or CC BY-SA text, is a training input. The gate is the **source** of
each sentence: every provenance row names its source, the allowed sources are listed in one place
(`tools/WordSense.Builder/ProvenanceSources.cs`) and documented with their evidence in "Sources" below, and the test
`CorpusLicenseTests` fails on any other source. The UD English-EWT treebank (CC BY-SA 4.0) is used **only to evaluate**,
from a git-ignored download, and is never committed or shipped.

Earlier waves had also read the Tatoeba sentences under `CC BY 2.0 FR` (9,296 sentences, 10,328 rows with their chooser
rows) and the corpus was relabeled from an earlier class-labeled corpus that held Wikipedia, Wiktionary and UD rows;
none of these rows is in the corpus any more.

## Layout

| Folder | File pattern | Purpose |
| ------ | ------------ | ------- |
| `anchors/` | `*.tsv` | Labeled training sentences (fine senses) |
| `holdout/` | `*.tsv` | Labeled evaluation sentences, never used for training or for choosing thresholds |
| `choosers/anchors/`, `choosers/holdout/` | `*.tsv` | Training and evaluation rows of the form choosers (the lemma or tense of a shared form) |
| `realtext/` | `*.tsv` | Real-text sentences for evaluation only (never trained on) |
| `provenance/` | `*.tsv` | Source of every sentence: `id`, `split`, `source`, `source_key` (see "Real Sentences and Provenance") |
| `coverage/` | `*.tsv`, `README.md` | The quota report of every sense (anchors and held-out sentences against the tiers) |
| `curation/` | `*.tsv`, `README.md` | The reviewed senses and groups (the input of `seed-inventory`) |
| `lexicon/` | `*.tsv` | The dictionary word list, the auxiliaries and the form rules (inputs of `compile-image`) |
| `open/` | see `open/README.md` | The open English part-of-speech corpus (CC0 annotations over public-domain text): `representative/` (random; the only input of usage statistics, such as the Basic tagger counts in `open/counts/`, and the headline real-text evaluation) and `targeted/` (deliberate selections; a challenge set) |
| `suite/` | `*.tsv`, `README.md` | The reviewed suite of 348 sentences about common ambiguous words, the expectation of each model for each row (a regression test) and the floors of the challenge set; project-authored, CC0 1.0, evaluation only |

Every file is UTF-8 without a byte-order mark, with LF line endings.

**Two kinds of corpus, two uses.** The encoder corpus (`anchors/`, `holdout/`, `choosers/`, `realtext/`) is *targeted by
nature*: each row was found or written for one sense or form, so the share of a sense in it says nothing about how often
the sense occurs. It trains classifiers (the encoder weights and the Basic cue tables) and never feeds a usage statistic;
the encoder weights even correct for it: the builder divides the share of each sense among the anchors of its lemma out
of the classifier scores and multiplies in the WordNet 3.0 usage of the sense (the usage prior, see
`docs/user_guide/accuracy.md`). Usage statistics (tag counts per word, tag transitions) come only from
`open/representative/`.

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
They are project-authored (source `llm-corpus`). The provenance id of a row is the first 12 hexadecimal digits of the
SHA-256 of `lemma|sentence` (`form|sentence` or `label|sentence` for a chooser row).

The `relabel-*.tsv` files hold the sentences of the earlier class-labeled corpus that were relabeled to fine senses (by
rule, or by `gpt-6.1-sol`); their provenance rows (`provenance/relabel.tsv`) name the original source.

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
and `quota-w<N>-forms.tsv` (chooser rows of a form), made by the quota filling (`tools/quota-fill`, see its README and the method below): they bring every sense to the quota
of its tier (see `coverage/README.md`).

**Method of the quota filling.** Per wave of lemmas (ordered by the WordNet 3.0 tag count):

1. *Pool.* Sentences of 6 to 34 words, deduplicated, from the documents that are allowed as real text (about 600,000
   sentences: Gutenberg books, FAA, Tatoeba CC0, VOA, NPS, USGS and National Weather Service); a sentence that the corpus
   or the suite already holds is never a candidate. The FAA handbooks enter text only, and a page that carries a credit
   to a third party is dropped whole (the documents that are used and the ones that are excluded are listed under
   "Sources").
2. *Harvest and label.* For each lemma a sample of candidate sentences with a form of the lemma is drawn (round robin
   over the forms; about 55% Gutenberg, 15% Tatoeba and 30% government text; at most 5 per document). A labeler model
   (`gpt-6.1-sol`) sees the sentence, the target and the full sense list (id, part of speech, gloss) of every lemma and
   part of speech that the lexicon allows for the form, and answers one sense with a confidence or `unsure`; only
   high-confidence answers are kept (about 88% of the candidates get a label).
3. *Selection.* For each sense with a deficit, round robin over forms and documents (at most 3 sentences of a sense and
   10 of a lemma from one document), no near-duplicate (token Jaccard 0.8) of a corpus or suite sentence of the lemma.
   Held-out sentences are filled first with real text, the anchors take the rest. A document gives either anchors or
   held-out rows (an evaluation document is chosen by hash, one in four), so the held-out rows are disjoint from the
   anchors by document; a document of the older rows that already supplies both may supply more anchors. The tier
   waves use the technical FAA text for the technical senses only (`--faa-tech-only`), and an FAA sentence with a
   figure reference or a word split by the PDF extraction is always dropped.
4. *Extra real anchors.* The senses that took part in a fine top-1 error of the encoders on the real *calibration* rows
   (never the test split) get a target of 8 real anchors (5 for one error), and a sense of an earlier wave with fewer
   than 3 real anchors gets 3.
5. *Top-up by writing.* Where real text cannot fill a sense, `gpt-5.6-luna` writes about 1.5 sentences per missing one
   from the gloss, the dictionary examples, the allowed forms and the other senses of the lemma, and `gpt-6.1-sol`
   labels them blind (the intended sense is not shown); a sentence is kept only when the blind labeler answers the
   intended sense with high confidence (about 82% to 85% are confirmed; only about 33% to 36% for the senses whose
   nearest sibling sense the labeler prefers, which a second writer prompt did not change). Mechanical checks: one braced
   target, the form allows the part of speech of the sense, 6 to 30 words, ASCII, no duplicate.
6. *Choosers.* A chooser needs rows for every lemma of a form: a lemma-choice prompt harvests them, the sentences of the
   fine-sense rows of a shared form are chooser rows too, and the forms that real text could not fill get written
   sentences that the blind labeler confirms.
7. *Technical senses.* Maintenance and engineering text uses words in senses that the encoders had not seen (`arm`,
   `seal`, `bearing`, `bleed`, `torque`, `run`, `lead`, `flush`, `tap`, `mount`): twelve technical lemmas joined the
   curation, six project senses were added to existing entries (`mount#mounting-fixture`, `bleed#vent`, `arm#prime-system`,
   `lead#electrical-lead`, `flush#fluid-flush`, `tap#tapping-point`), `curation/tech-senses.tsv` lists the senses and
   their extra anchors, and the training data got real FAA anchors (a document supplies at most 8 sentences of a sense)
   and written simplified-technical-English sentences (at least 8 for each technical sense). Giving all 114 participial
   adjective entries of the dictionary their own analysis was measured and reverted: the real-text fine top-1 fell about
   two points, because the evaluation corpus labels forms such as `based`, `covered` and `driven` with the verb senses
   only, so only the adjectives that technical text uses are listed (`lexicon/own-adjectives.tsv`).
8. *Not filled.* A sense that cannot be filled credibly with this method (it is a near-duplicate of a sibling, or its
   gloss is too thin to write to) is listed with the number of sentences written and confirmed in
   `coverage/quota-unfilled.tsv`; these senses are candidates for the group and curation review. A probe of the FAA
   sentences that the labeler could not place (a review by `gpt-6.1-sol`) finds meanings that the catalog lacks (listed in
   `curation/README.md`).

**Every quota-filled row is targeted.** It was found or written for one specific sense (or, for the chooser files, one
specific form), never drawn at random from a text. Its provenance row has the source of the sentence and the key of its
document (`source_key`: `pg<ebook>#<ordinal>` for a Gutenberg book, `<document id>#<ordinal>` for an article or a page,
the Tatoeba sentence id, or `quota-w<N>-written` for a sentence written by a model); the licence follows from the
source (see "Sources").

**Wave 3 files.** `quota-w3-*` fills the lemmas of ranks 121-200 and the technical lemmas, `quota-w32-written.tsv` is the
second round for senses that stayed short (written with another prompt), and `quota-w33-real.tsv` and
`quota-w33-written.tsv` are the technical senses of `curation/tech-senses.tsv`. `quota-w<N>-forms*.tsv` are chooser
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

Each sentence of `anchors/`, `holdout/`, `choosers/` and `realtext/` has a row in `provenance/*.tsv`, found by its id
(the first 12 hexadecimal digits of the SHA-256 of `lemma|sentence`; `form|sentence` or `label|sentence` for a chooser
row). A provenance file has a header and four tab-separated columns:

| Column | Content |
| ------ | ------- |
| `id` | The id of the sentence |
| `split` | `anchors` or `holdout` |
| `source` | The source of the sentence: one of the allowed sources of "Sources" (`gutenberg`, `voa`, `usgs`, `nps`, `nws`, `faa`, `tatoeba-cc0`, `llm-corpus`, `authored`) |
| `source_key` | The document of the sentence: `pg<ebook>#<ordinal>`, `<document id>#<ordinal>`, a Tatoeba sentence id, or the batch of a written sentence |

These are the columns that the code reads: the builder takes the document (`source` and the part of `source_key` before
`#`; a Tatoeba sentence is its own document) for the split of the real held-out sentences by document and for the
documents that supply anchors (`RealSplit.cs`), the quota-filling tools take the same for the roles of the documents.
Nothing else is recorded per row: the licence follows from the source.

**The licence check.** The allowed sources are defined in one place, `tools/WordSense.Builder/ProvenanceSources.cs`. The
test `CorpusLicenseTests` fails when a provenance file has other columns, when any row names a source that is not
allowed, or when a sentence of the sense, chooser or real-text files has no provenance row. A source is added to the list
only together with its evidence in "Sources". The corpus is not shipped in the NuGet package.
`realtext/relabel-realtext.tsv` holds real sentences for evaluation only (never trained on).

## Sources

Every sentence comes from one of these sources.

| Source (`source`) | What it is | Licence basis and evidence |
| ----------------- | ---------- | -------------------------- |
| Project Gutenberg, books published 1930 or earlier (`gutenberg`; key `pg<ebook>`) | The 175 books listed in `open/sources.tsv` (header, footer and licence stripped; translations excluded) | US public domain: published 1930 or earlier (term of 95 years from publication for works published before 1978, <https://www.copyright.gov/help/faq/faq-duration.html>), and the landing page of each book states "Public domain in the USA". "The vast majority of Project Gutenberg eBooks are in the public domain in the US" <https://www.gutenberg.org/policy/permission.html>; "If you strip the Project Gutenberg license and all references to Project Gutenberg from the text, you are left with a text unrestricted by U.S. intellectual property law" <https://www.gutenberg.org/policy/license.html>. Book by book evidence: `open/gutenberg-books.md` and `open/sources.tsv`. |
| VOA Learning English, staff-written articles (`voa`; key `voa-<article id>`) | The 379 articles listed in `open/sources.tsv` (articles with an AP, AFP or Reuters marker, or any copyright marker, are excluded) | Work of US federal employees, 17 U.S.C. §105(a): "Copyright protection under this title is not available for any work of the United States Government" <https://www.law.cornell.edu/uscode/text/17/105>. Agency statement: "All text, audio and video material produced exclusively by the Voice of America is in the public domain" <https://www.voanews.com/p/5338.html>. |
| USGS (`usgs`; key `usgs-<page>`) | Prose of the 443 pages (mostly the Water Science School) listed in `open/sources.tsv` (pages with a copyright marker are excluded) | §105(a), and "USGS-authored or produced data and information are considered to be in the U.S. Public Domain" <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits>. |
| National Park Service (`nps`; key `nps-<park>-<page>`) | Prose of the 363 pages listed in `open/sources.tsv` (pages with a copyright marker are excluded) | §105(a), and "material created by the National Park Service and presented on this website, unless otherwise indicated, is generally considered in the public domain" <https://www.nps.gov/aboutus/disclaimer.htm>. |
| NOAA National Weather Service (`nws`; key `nws-<page>`) | Prose of the 200 weather.gov and NOAA JetStream pages listed in `open/sources.tsv` | §105(a), and "The information on National Weather Service (NWS) Web pages are in the public domain, unless specifically noted otherwise" <https://www.weather.gov/disclaimer> (the same page asks that nobody claims the information as their own: the corpus makes no claim on the sentences). |
| FAA handbooks and advisory circulars (`faa`; key `faa-<document>`) | The documents listed in `tools/quota-fill/faa.py`: the three Aviation Maintenance Technician Handbooks (FAA-H-8083-30B, -31B, -32B), the Pilot's Handbook of Aeronautical Knowledge (FAA-H-8083-25C), the Airplane, Helicopter, Glider and Balloon Flying Handbooks (FAA-H-8083-3C, -21B, -13B, -11B), Plane Sense (FAA-H-8083-19A) and Advisory Circulars 43.13-1B and 43.13-2B; text only, and every page that carries a credit to a third party ("courtesy of", "used with permission", "copyright", a copyright sign, "reproduced", a photo credit) is dropped whole | §105(a): the documents are works of FAA employees. The FAA site carries no copyright or public-domain statement of its own (checked <https://www.faa.gov/web_policies/> on 2026-10-20), so the statute alone is the basis; this is why the FAA is not a source of the open corpus, which accepts only agencies with an explicit statement. Documents with third-party rights (the Parachute Rigger Handbook, the Aviation Weather Handbook, Aerodynamics for Naval Aviators, the MC-4 manual) are not used. |
| Tatoeba, CC0 export (`tatoeba-cc0`; key = the sentence id) | The sentences of the official CC0 export `sentences_CC0` <https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2>; each sentence is on <https://tatoeba.org/en/sentences/show/> followed by its id | CC0 1.0: "A part of our sentences are also available under CC0 1.0" <https://tatoeba.org/en/downloads>. Sentences that are only under CC BY 2.0 FR are not used. |
| Project-authored sentences (`llm-corpus`, `authored`) | Sentences written for the project by language models (`gpt-5.6-luna`, verified or selected by other models; the sentences of the earlier class-labeled corpus were written by language models too) and sentences written by the project | Dedicated to the public domain under CC0 1.0 by the project owner. There is no third-party source text, so a search of the sources above finds nothing for them. |

**Checking one sentence.** Anyone who questions the provenance of a specific sentence can compute its id, read its
provenance row (`source`, `source_key`), download the listed source material (the documents of `open/sources.tsv`, with
their URLs and SHA-256 values, for the Gutenberg, VOA, USGS, NPS and NWS sentences; the FAA documents of
`tools/quota-fill/faa.py`; the Tatoeba CC0 export) and search the document for the sentence. A real sentence is the
text of the document with plain ASCII quotes and dashes and single spaces. Excluded for licence reasons and never used:
Wikipedia, Wiktionary, Universal Dependencies text, CC BY and CC BY-SA text, Tatoeba CC BY sentences, news sites, NOAA
National Ocean Service, NASA and CDC pages (`open/README.md`, section 4).

## Disjointness of the Parts

The parts of the corpus must not share a sentence in a way that leaks an evaluation sentence into training:
`WordSense.Builder check-leakage` compares the normalized sentences (case, spacing and punctuation do not make a sentence
new) of the anchors, the chooser anchors and the training split of the open corpus (training) with the held-out,
chooser held-out and real-text sentences, the reviewed suite (`suite/`), the development and test splits and the
challenge set of the open corpus, the example sentences of Open English WordNet (CC BY 4.0, so none may be in this
CC0 corpus) and, when present in their git-ignored caches, SemCor and the treebank test file; it exits with 1 on a leak.
`CorpusTests` runs the same check on the committed corpus. Overlap that remains by design: training-with-training
(anchors with the chooser anchors and the open corpus training split: duplicates, nothing is evaluated on them), the
chooser held-out rows that are views of held-out rows, one three-word stock phrase that is also an Open English WordNet
example, and three SemCor sentences that are also open corpus sentences (the SemCor evaluation leaves out such a sentence
rather than removing it from the corpus). The runs of eight words that two parts share (a few dozen) are reported as
informational. The reviewed suite, the real-text rows and the challenge set overlap nothing.

## Results

- **Part-of-speech audit (two blind judges).** The 15,864 anchor and held-out rows of the earlier class-labeled corpus, as
  it stood when it was relabeled to fine senses (before the quota filling; 14,766 of these rows are still in the corpus,
  as `relabel-*.tsv` rows), were judged for the part of speech of the braced word by two independent judges who saw only
  the lemma and the sentence, never the label. They agreed on 15,827 rows (99.77%); the 37 rows on which they disagreed
  and the 42 on which they agreed with low confidence (79 rows) went to a third judge who saw both answers. The third
  judge settled 15 of them and 64 stayed undecided; the undecided rows were never used as evidence. A blind re-judging of
  every 20th accepted row (790 rows) found no disagreement. The audit was not repeated for the rows added later, whose
  senses are checked by the blind labeler of the quota filling (see the method above).
- **Quota filling.** About 88% of the harvested candidates received a high-confidence sense label; of the sentences
  written for a missing sense, about 82% to 85% were confirmed by the blind labeler (about 33% to 36% for senses whose
  nearest sibling sense the labeler prefers). `coverage/quota-coverage.tsv` has the quota status of every sense.
- **Waves 4u and 4r (uncommon and rare tiers).** After wave 4c (the common tier), two tier waves filled the short senses
  of the uncommon tier (`4u`: 10 anchors and 3 held-out sentences per sense) and of the rare tier (`4r`: 5 and 2),
  harvested with `--tier U` and `--tier R` (and `--faa-tech-only`). Real rows: `4u` 3,934 anchors and 99 held-out
  sentences (from 38,665 candidates, 33,458 labeled, 30,385 with high confidence), `4r` 2,129 anchors and 49 held-out
  sentences (from 23,370 candidates, 20,111 labeled, 18,501 with high confidence). Written rows: `4u` 3,984 anchors and
  2,267 held-out sentences (blind labeler agreed on 11,103 of 14,207 written sentences, 10,612 of them with high
  confidence), `4r` 4,420 anchors and 2,725 held-out sentences (13,917 of 17,262, 13,175 with high confidence). Chooser
  rows follow the fine-sense rows (`4u` 773 + 793 anchors and 8 + 429 held-out; `4r` 491 + 790 and 12 + 498; real +
  written). **Real held-out text was nearly exhausted.** The evaluation documents (one in four, chosen by hash) of the
  sentence pool supply held-out rows only, and one that already supplies anchors supplies none, so the harvests held
  only 198 (`4u`, mostly Tatoeba) and 61 (`4r`) candidates of evaluation documents;
  the held-out sentences of these two waves are therefore mostly written (and blind-labeled), and the real test split
  grew by only 69 sentences (2,328 to 2,397). After the two waves 6,198 of the 7,035 senses of the trained lemmas meet
  their quota (`WordSense.Builder tiers`).
- **Open corpus annotation** (`open/quality.md`): against UD English-EWT dev (300 sentences, 3,597 tokens, evaluation
  only) the primary annotator scores 95.05% UPOS and 96.02% collapsed tag after mapping the reference to the lexicon
  conventions (90.63% and 91.60% raw); lemma accuracy 98.36%. The second annotator cross-checked 10,449 of 14,980
  sentences (166,802 tokens): 94.47% UPOS agreement, kappa 0.938; 36.25% of the tokens are `unchecked`.
- **Leakage.** `check-leakage` finds no evaluation sentence in a training part of the committed corpus (the overlap that
  remains is the one listed above).
- **Blind relabeling of the real held-out rows.** All 4,615 real-text sense held-out rows (calibration and test sides)
  were labeled again by a blind labeler that saw the sentence and the senses of its lemma and part of speech, never the
  gold label: 4,041 agreed, 255 disagreed and 319 were unsure. A second blind judge decided the 574 rows that were not
  agreed (candidates in random order, the gold label never marked): 321 kept their gold sense, 166 were relabeled (158 to
  the sense of the first blind labeler, 8 to another listed sense) and 87 were removed (no listed sense fits). No model
  was consulted.
- **Blind relabeling of the real anchor rows.** The same check was run on all 20,059 real-text anchor rows: 18,350
  agreed, 881 disagreed and 828 were unsure, and the second blind judge decided the 1,709 rows that were not agreed:
  1,055 kept their gold sense, 469 were relabeled (454 to the sense of the first blind labeler, 15 to another listed
  sense) and 185 were removed together with their chooser and provenance rows. The 469 relabels were then confirmed by
  two further independent blind judges of different model families (`claude-sonnet-5.5` and `gpt-6.1-sol`; candidates
  in random order, the original not marked): a relabel stayed only when
  both chose the new sense, which held for 225 rows; the other 244 were reverted to their original label, so the check
  left 225 relabeled and 185 removed anchor rows. A seeded sample of 230 written
  (`llm-corpus`, `authored`) anchor rows was labeled blind only, as a measure: 203 agreed, 7 disagreed and 20 were
  unsure (11.7% not agreed, against 8.5% for the real anchors); no written row was changed.

## Label Reconciliation

The sense labels of a lemma come from its anchor sentences. The labels of a listed form are authoritative for that
form: every anchor, holdout and real-text sentence whose braced form is listed must carry a sense that the labels
name, otherwise the build fails. Where they disagree, widen the labels of the form (the forms file) rather than
dropping the sentence.

<!-- markdownlint-enable MD013 -->
