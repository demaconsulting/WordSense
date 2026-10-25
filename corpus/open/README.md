<!-- markdownlint-disable MD013 MD060 -->

# Open English POS Corpus

An independent, openly licensed English part-of-speech corpus built for the WordSense project as an alternative to UD English-EWT (CC BY-SA 4.0). It lives in the repository as `corpus/open/`; the scripts that built it are in `tools/open-corpus/` (section 10).
It has two parts with different purposes, kept apart on purpose (section 1a): **`representative/`**, a random sample of 14,980 sentences (261,641 tokens, 5,310 source documents, about 3,745 sentences in each of four genre groups) that is the only input for usage statistics and "real text" evaluation,
and **`targeted/`**, sentences chosen deliberately to exercise specific uses (so far the 32 simplified-technical-English sentences of the challenge set, section 1b). Everything is annotated with UPOS, a collapsed WordSense tag, lemma and a few morphological features, and a per-token consensus status. The tag counts that train the statistical tagger of the Basic model are derived from the representative training split and live in `counts/` (section 1c).
The numbers in this README describe the representative part unless stated otherwise.

**Current state.** The primary annotation (gpt-6.1-sol) is complete for every token. The independent second-annotator cross-check (gemma4:26b) covers 10,449 of the 14,980 sentences; the tokens of the other 4,531 sentences have consensus status **`unchecked`** (36.25% of tokens), the tokens of the checked sentences `unanimous` or `disputed`. The accuracy figures of `quality.md` for gpt-6.1-sol alone on the UD-EWT dev sample apply to every token regardless of status. The raw outputs of the annotators are not kept in the repository (section 7).

**License (final, owner decision of 2026-10-20): our annotations and our selection are released under CC0 1.0** (`LICENSE` is the CC0 1.0 legal code). The underlying sentences are public domain in the United States or CC0 per source (section 3); we make no claim on them.
Nothing here is legal advice.

## 1a. The two parts and their rules

| | `representative/` | `targeted/` |
|---|---|---|
| What | Random sample within sources and genres (section 5; seed 20261019) | Sentences deliberately chosen to exercise specific uses: rare senses, hard conventions (passive vs adjectival `was closed`, phrasal-verb particles), chooser forms (`read`, `left`), disputed patterns, weak genres, error-driven cases |
| Files | `representative/data/{train,dev,test}.tsv` (document split 80/10/10) | `targeted/data/{train,test}.tsv` (document split 80/20; the test part is the **challenge set**) |
| Use for usage statistics (tagger counts, frequency priors) | **Yes: the only permitted input** | **Never** |
| Use for headline "real text" evaluation | **Yes** (dev/test) | No; report the challenge set **separately**, never merged into a headline number |
| Use for training classifiers | Yes | Yes (train part) |
| How it grows | Only by the same random sampling procedure, with recorded seeds (`scripts/select_sentences.py`, `build/dedupe_report.json`) | Any deliberate selection, but every sentence carries `# selection` and `# query` lines |
| Extra sentence-level comments | none (a `selection` or `query` line is a validation error) | `# selection = <key>=<value>` and `# query = <how it was found>`, both required |

**Why.** Usage statistics (how often a word is a noun or a verb, tag priors, sense frequencies) and a real-text accuracy figure are only meaningful if the sentences are an unbiased sample of text. A deliberately chosen sentence is there *because* it is unusual or hard: adding such sentences to the sample would inflate rare senses, depress accuracy and
distort every count derived from it. Keeping them in a separate part lets us add as many hard cases as we want for training and for a challenge evaluation without touching the statistics. **Never add hand-picked sentences to `representative/`.**

**Selection key (targeted).** `# selection = <key>=<value>` with the key one of `sense` (`sense=bow#violin-bow`), `convention` (`convention=passive-vs-adjective`), `form` (`form=read`), `genre` (`genre=informal`), `error` (`error=<model>:<case>`), `pattern` (a disputed pattern, `pattern=ADJ/NOUN-modifier`).
`# query = ...` says how the sentence was found (the search expression, the model run, the list or the rule), so the choice can be reproduced and audited.

**Split rule and leakage.** Both parts split by document with the same hash bucket (`int(SHA-256(source_id)[:8], 16) % 100`). Representative: 0-79 train, 80-89 dev, 90-99 test. Targeted: 0-79 train, 80-99 test. A document is therefore on the training side in both parts or on the evaluation side in both parts, so training on `targeted/train` never sees a document that `representative/dev` or `representative/test` evaluates on.
`validate.py` also rejects a targeted sentence whose text or `sent_id` duplicates one in the representative part. Targeted sentences must come from sources listed in `sources.tsv` (same license bar as everything else: add new documents there first).

**Status.** `targeted/` holds the simplified-technical-English challenge set (section 1b) in `data/test.tsv`; `data/train.tsv` has the header only. Sentences added there are annotated with the same guideline and pipeline as the representative part (`split_data.py --part targeted`); their consensus status works the same way.

## 1b. The simplified-technical-English challenge set

`targeted/data/test.tsv` holds 32 sentences (272 tokens) written by the project: 30 imperative maintenance instructions with component names (`Theta Arm`, `Gantry Robot`, `Z-Axis Carriage`), part numbers (`P/N 4471-20`), units (`N·m`, `psi`), negated imperatives and warnings, plus two `Mount ... home` sentences that exercise the verb and adverb readings of `mount` and `home`. Every sentence has `# selection = genre=technical-instructions` and `# query = written:ste-probe`; the source document is `wordsense-ste-probe` (in `sources.tsv`, family `project-written`; the project owner dedicates the sentences to the public domain under CC0 1.0). The document hash bucket is 95, so the set is in the test split of the targeted part, a challenge set that is never merged into a headline number.
The parts of speech were annotated independently by two annotators with the 10-class WordSense tags, who agreed on every token; the UPOS, lemma and features follow `GUIDELINE.md` (the infinitive marker `to` and `not` are PART here, where the 10-class gold had a preposition and an adverb, which that tag set cannot express). The consensus status of its tokens is `unanimous`, meaning that two annotators agreed, not that gemma agreed. Component names are NOUN or ADJ by function, never PROPN. The set is a project-written probe, not running text, and is small: 32 sentences are a smoke test and not a measurement of accuracy on technical text. The sentences with their annotations are in `targeted/data/test.tsv`.

## 1c. The tag counts

`counts/word-tags.tsv`, `counts/tag-ngrams.tsv` and `counts/tag-clause-ngrams.tsv` are the usage statistics that train the part-of-speech tagger of the Basic model: the tag counts of every token of `representative/data/train.tsv` (all consensus statuses; a contraction that the corpus splits, `do` and `n't`, is joined into one token as an application passes it, and the possessive `'s` stays apart), in the 13 tagger tags, written by `dotnet run --project tools/WordSense.Builder -- open-counts` (11,990 sentences, 207,893 tokens, 17,719 words). `tag-clause-ngrams.tsv` holds the bigram and trigram counts of the positions whose clause already has a verb (a clause starts at the start of the sentence and after a conjunction or punctuation tag, and has a verb from the first verb tag on); the counts of the other positions are the difference to `tag-ngrams.tsv`. They are derived from the representative part only, never from `targeted/` and never from the balanced sentence corpus of the repository, and are CC0 1.0 like the annotations.

## 1. Contents

| Path | What it is |
|---|---|
| `README.md` | This document: sources, licenses, method, formats, reproduction, limitations |
| `LICENSE` | CC0 1.0 Universal legal code (annotations and selection) |
| `GUIDELINE.md` | Our own annotation guideline, **version 4**, including the UPOS -> WordSense collapsed mapping and the conventions checked against the WordSense lexicon (section 9) |
| `quality.md` | Measured accuracy (UD-EWT dev sample, raw and convention-mapped), agreement, and the evidence for the annotation policy |
| `sources.tsv` | One row per source document used (5,310): family, title, URL, author, year, license basis (with the quoted statement), terms URL, retrieval date, SHA-256 of the retrieved file and of the extracted text, sentences contributed |
| `gutenberg-books.md` | The 175 Gutenberg books with the recorded publication year and our evidence, for the owner's spot check (`sources/gutenberg_years.tsv` is the machine-readable form) |
| `terms.tsv` | The terms/license pages read, with status (used or excluded), retrieval date, SHA-256 and the key sentence found |
| `sources/urls.tsv`, `sources/sources_excluded.tsv`, `sources/fetch_log.tsv`, `sources/discovered/` | The pinned input list (2,181 candidate documents), the candidates rejected and why, the retrieval date and SHA-256 of every raw download, and the discovery listings |
| `representative/`, `representative/data/{train,dev,test}.tsv` | The random-sample part of the annotated corpus (14,980 sentences; format in section 9; rules in section 1a and `representative/README.md`) |
| `targeted/`, `targeted/data/{train,test}.tsv` | The targeted part (the 32 simplified-technical-English sentences in the test split so far; rules and format in section 1a and `targeted/README.md`) |
| `counts/` | `word-tags.tsv`, `tag-ngrams.tsv` and `tag-clause-ngrams.tsv`: the tag counts of the representative training split that train the Basic tagger (section 1c) |
| `build/` | `sentences.tsv` (the 15,000 selected sentences), `sentences_final.tsv` (14,980 after the post-selection exclusion), `post_exclusions.tsv`, `dedupe_report.json`, `documents.tsv`, the pilot inputs |
| `eval/` | `ud_dev_sample_ids.txt` (ids only), `ud_dev_scores.json` (accuracy measurement), `corpus_scores.json` (agreement on the corpus) |
| `prompts/` | `fewshot.txt` (examples from our own pilot sentences) and `fewshot_ids.txt` |
| `../../tools/open-corpus/` | All code (Python, standard library plus `lxml` and `pypdf`; `requirements.txt`); the git-ignored `cache/` (raw third-party downloads, including any treebank file) and `work/` (scratch) are there, not here |

## 2. Independence statement

This is our position, stated as precisely as we can; it is not legal advice.

1. **No UD text, annotations or guideline text were used as input.** No sentence comes from a Universal Dependencies treebank. The sentence texts of UD-EWT train and dev were used **only as a negative filter**: a candidate sentence whose normalized text exactly matches one of them is dropped (134 candidates were dropped this way);
   nothing from UD is stored or shown to a model. No UD tag or lemma was shown to any annotator, and no UD documentation was given to any model or copied into `GUIDELINE.md`.
2. **The guideline was written independently, and its hard-case list is built from our own pilot data only.** `GUIDELINE.md` and the few-shot examples were authored for this corpus in our own words with our own examples; it uses the 17 UPOS tag *names* (facts) and the names of eight morphological features.
   Section 4 was derived from the disagreements between our annotators on two pilot draws of our own sentences: **round 1** (first draw, tagged with version 1 by claude-sonnet-5.5 and gemma4:26b: 732 of 14,334 tokens, 5.1%, disagreed; this gave version 2 and the few-shot examples), and **round 2** (second draw of 1,000 sentences, 17,376 tokens, tagged with version 2 by gpt-6.1-sol and compared with gemma4:26b, 807 tokens or 4.64%, and with claude-sonnet-5.5, 112 tokens or 0.64%; a gpt-6.1-sol sub-agent proposed rules for every group of about 10 or more disagreements, which the project edited into version 3, ordered by frequency). Version 4 changed one convention (`be`) after checking the WordSense lexicon (`GUIDELINE.md` section 9). The few-shot examples are 20 sentences of our own first pilot draw, annotated by hand.
3. **An earlier draft was UD-informed and is superseded.** Version 1 of the guideline had a hard-case list that was partly chosen from error categories (not text) found in an exploratory accuracy test of LLM taggers against UD-EWT dev, as listed by the project owner. That draft is superseded: no published annotation was produced with it,
   and the hard-case list of version 4 does not use it. We cannot claim zero influence: the first pilot draw was tagged with version 1 and its disagreements were the data for round 1 (version 2); versions 2 to 4 reverse several version 1 choices and add many rules that version 1 did not have, but the lineage exists and is documented.
   The drafts are not kept in the repository. The guideline's authors are LLMs, which may know treebank conventions from pretraining.
4. **Sources and licenses are listed per document** in `sources.tsv` (license basis, quoted statement, terms URL, retrieval date, SHA-256).
5. **All annotations were produced by the listed models from the listed prompts** (section 7): gpt-6.1-sol (primary, every token) and, for 10,449 sentences, gemma4:26b (second annotator, flag only). No tag was edited by hand. Mechanical steps only: validation, removal of features not allowed on the final UPOS, re-asking sentences that failed validation, and dropping the sentences of one source found unsuitable after selection (section 4).
6. **UD-EWT dev was used only to measure annotation accuracy** (300 sentences, ids in `eval/ud_dev_sample_ids.txt`), never as input to annotation, prompting, tuning or training. UD test was never used. No UD text or tags are stored in this folder. The convention map used for the headline accuracy (`quality.md`) maps *UD labels to our conventions for scoring*; it does not touch the corpus.
7. **Possible pretraining exposure.** The LLMs may have been exposed to public treebanks, including UD, during pretraining; that cannot be ruled out. Our position is that this does not make the corpus a derivative of UD: the sentences are our own selections from other sources, and every tag, lemma and feature was generated per sentence by the models from our guideline and prompts, as facts about those sentences
   (a part of speech is a fact about a word in context, not a copy of any treebank's expression). We cannot prove the models never reproduced a memorized annotation. Our conventions follow the WordSense lexicon and differ from UD's in documented ways (possessives are DET, particles of phrasal verbs are ADV, prepositions before gerunds are ADP), visible in the raw accuracy against UD (`quality.md`).

## 3. Sources and license basis

Only agencies whose **own site carries an explicit public-domain statement** are used (owner rule). Terms checked and retrieved on **2026-10-19** (hash and key sentence per page in `terms.tsv`). Raw downloads: Gutenberg texts (31 books) and the Tatoeba export were reused from a local cache of 2026-10-15
(retrieval date = file date, recorded in `sources/fetch_log.tsv`); all other pages were downloaded on 2026-10-19 or 2026-10-20.

| Family | Sentences | Documents | Evidence | Statement (exact quote) and URL |
|---|---|---|---|---|
| Project Gutenberg | 3,730 | 175 books | **verified** (book-level years: spot-check list in `gutenberg-books.md`; ebook 7280 owner-verified) | US public domain: published 1930 or earlier; each landing page states "Public domain in the USA". Gutenberg license: "If you strip the Project Gutenberg license and all references to Project Gutenberg from the text, you are left with a text unrestricted by U.S. intellectual property law" <https://www.gutenberg.org/policy/license.html>; "The vast majority of Project Gutenberg eBooks are in the public domain in the US" <https://www.gutenberg.org/policy/permission.html>. |
| VOA Learning English | 3,750 | 379 articles | **verified** (VOA-only articles) | "All text, audio and video material produced exclusively by the Voice of America is in the public domain" <https://www.voanews.com/p/5338.html>; AP, AFP and Reuters material is licensed and excluded. |
| NOAA National Weather Service (weather.gov, NOAA JetStream) | 771 | 200 pages | **verified** | "The information on National Weather Service (NWS) Web pages are in the public domain, unless specifically noted otherwise" <https://www.weather.gov/disclaimer>. |
| USGS (Water Science School) | 1,670 | 443 pages | **verified** | "USGS-authored or produced data and information are considered to be in the U.S. Public Domain" <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits> (the page also says some non-USGS images and graphics are used with permission; pages with copyright markers are excluded; text only). |
| US National Park Service | 1,309 | 363 pages | **verified** | "material created by the National Park Service and presented on this website, unless otherwise indicated, is generally considered in the public domain" <https://www.nps.gov/aboutus/disclaimer.htm> (third-party and trademarked material is excluded by the same page; pages with copyright markers are excluded). |
| Tatoeba | 3,750 | 3,750 sentences | **verified** via official CC0 export | Only sentences in the official `sentences_CC0` export <https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2>; "A part of our sentences are also available under CC0 1.0" <https://tatoeba.org/en/downloads>. No contributor names in the export. No per-sentence page check was possible with a script. |
| **Total** | **14,980** | **5,310** | | |

The totals describe the representative part. The 32 sentences of the targeted challenge set (section 1b) are project-written (family `project-written` in `sources.tsv`, CC0 1.0 by the project owner) and not part of this table.

The US term rule used for Gutenberg is 95 years from publication for pre-1978 works (copyright.gov FAQ in `terms.tsv`); works published in 1930 or earlier are in the US public domain in 2026. 17 U.S.C. 105(a) (no copyright protection for works of the US Government) is the statutory basis for the agency pages;
105(b) does not apply to these agencies. NWS asks that you do not claim copyright in its information: the CC0 dedication covers our annotations and selection only, not the underlying sentences.

**Gutenberg qualification rule.** The 34 books of the pilot were chosen by publication year (1930 or earlier). The additional books must pass all of: the author died in 1930 or earlier, the language is English with no translator listed, the landing page states "Public domain in the USA", and a first-publication year of 1930 or earlier is supported by Open Library (CC0 data) or by the book's own front matter.
Five pilot books have authors who died after 1930 and rest on the publication year alone (ebooks 64317, 17625, 17211, 21959, 3825; the table lists the evidence). Ebook 7280 (John Burroughs, *My Boyhood*): "1922 — owner-verified (published 1922, posthumous memoir)".
Years recorded from Open Library can be approximate (round placeholders such as 1800 for old books); the table shows the author's death year and the front-matter years next to each, and notes the entries we know to be off.

**Project Gutenberg trademark.** We use only the book text between the START and END markers (header, footer, license, transcriber notes and any paragraph mentioning Gutenberg, an e-book or a transcriber are removed). `sources.tsv` credits the origin by link only, which the Gutenberg license treats as outside trademark use.
Gutenberg texts are public domain in the US only; users elsewhere must check their own law.

## 4. Exclusions and why

- **NOS (NOAA National Ocean Service)**: dropped by owner decision (2026-10-20): its disclaimer says "public information ... may be distributed or copied" but does not say "public domain". No NOS sentence is in this corpus.
- **FAA and NASA** (dropped from this corpus by owner decision): the FAA site carries no copyright or public-domain statement (statute only); NASA says its content is "generally not subject to copyright" and marks occasional third-party material, which is not an explicit public-domain statement. Their terms rows stay in `terms.tsv` with status `excluded`. (The encoder corpus of the repository does use FAA handbooks, under 17 U.S.C. §105 alone; see "Sources" in `../README.md`.)
- **CDC**: excluded. Its reuse page attaches conditions beyond 17 U.S.C. 105, the site blocks scripted access (HTTP 403), and it warns of contractor and licensed material. **MedlinePlus (A.D.A.M. encyclopedia)**: copyrighted.
- **Wikipedia, Wikinews, Wiktionary, any CC BY-SA text, news sites, social media, Common Crawl derivatives**: excluded by policy. **All Universal Dependencies text, tags and documentation**: excluded (independence). **Tatoeba CC BY sentences**: excluded; only CC0 sentences are used.
- **VOA**: 841 candidate articles were listed; those that contain an AP, Reuters, AFP or copyright marker anywhere in the article body (most "As It Is" and health/science stories are adapted from AP or AFP reports) or have no article body are rejected (`sources/sources_excluded.tsv`: 501 rejected candidates over all families, of which 436 for a third-party or copyright marker). Video captions, photo credits and glossaries are not used.
- **USGS/NPS/NWS**: only prose paragraphs are used; pages whose text contains a copyright marker are rejected; a few downloads failed or were empty (`sources/sources_excluded.tsv`).
- **Gutenberg**: translations are excluded by rule; one candidate with a translator was excluded. **Post-selection exclusion**: ebook 58560, *Index of the Project Gutenberg Works of E. W. Hornung*, was selected (20 sentences) but is an index compiled by Gutenberg volunteers rather than the author's text, so its authorship and license position are not covered by the author's publication date;
  its sentences are dropped from the corpus after the fact (`build/post_exclusions.tsv`, `scripts/apply_exclusions.py`), which is why the annotation inputs hold 15,000 sentences and the corpus 14,980. Also excluded: images, audio, video, logos, and any page whose license basis cannot be stated in one sentence.

## 5. Selection and sampling

Target mix: four groups of 3,750 sentences (selected 15,000): conversational (Tatoeba), news (VOA Learning English), technical/informational (USGS 1,670, NPS 1,309, NWS 771), literary/historical (Gutenberg 3,750, 3,730 after the exclusion). Deterministic, seed **20261019** (`scripts/common.py`); every random choice derives from it.

1. `harvest.py` extracts clean paragraphs per document (HTML through `lxml`; VOA from the article body; Gutenberg between the START and END markers).
2. `select_sentences.py` splits paragraphs into sentences (abbreviation-aware regex), normalizes typography (curly quotes to ASCII, dashes to `--`), tokenizes, and keeps sentences that pass: 4 to 40 tokens; start with a capital letter; end with `.`, `?` or `!`; balanced quotes and brackets; no URL, markup character or replacement character;
   no token with punctuation fused to letters; at most 3 numeric tokens and at most 20% digit characters (no tables or number lists); at least 60% alphabetic tokens; not mostly upper case; not a caption; no long title-case runs. Questions, imperatives and short dialogue lines are kept. The 20 few-shot sentences are excluded from the pool.
3. **Deduplication** (exact match ignoring case and punctuation): against the corpus itself, against the 1,000-sentence pilot, against the sentence texts of UD-EWT train and dev (13,367 keys; negative filter only), and against every sentence-like cell of the WordSense repository's `corpus/**/*.tsv` (42,396 keys, read-only).
   Candidates dropped: Tatoeba 3,006 (WordSense corpus 2,748, duplicates 258); VOA 1,951 (duplicates 1,925, UD 25, WordSense 1); technical 4,569 (duplicates 4,567, UD 2); Gutenberg 9,352 (duplicates 5,716, WordSense corpus 3,529, UD 107). Details: `build/dedupe_report.json`.
4. **Cap per document: 28 sentences** (the filter limit was 30; the realized maximum is 28), so no book or article dominates. Per group the target is spread evenly over the documents (seeded sampling, then seeded trimming or top-up to exactly 3,750); every Tatoeba sentence is its own document.
5. **The pilot is kept**: 948 of the 1,000 pilot sentences are part of the corpus (the 39 pilot NOS sentences left with NOS, and 13 more were dropped by the new filters or the UD/WordSense dedupe), and they were **re-annotated with the final guideline** together with everything else (the corpus has one uniform annotation pass).

Average sentence length: conversational 12.6 tokens, news 14.8, technical 22.0, literary 20.4.

## 6. Tokenization

`scripts/common.py: tokenize`, rule-based; the tokens always concatenate back to the text without spaces (validated):

- Typographic quotes and apostrophes are normalized to ASCII `"` and `'`; `--`, an em dash or an en dash becomes the token `--`.
- Punctuation and brackets are separate tokens; `...` is one token. A trailing period stays attached to a known abbreviation (`Mr.`, `Dr.`, `St.`, `etc.`, `U.S.`, `a.m.`, and a single capital initial such as `J.`, except at the sentence end). `No.` is in the abbreviation list, so an answer "No." stays one token.
- Clitics are split off: `n't` (`do` + `n't`; `can't` -> `ca` + `n't`, `won't` -> `wo` + `n't`, `shan't` -> `sha` + `n't`, `ain't` -> `ai` + `n't`), `'s`, `'re`, `'ve`, `'ll`, `'d`, `'m`; `cannot` -> `can` + `not`. `gonna`, `wanna`, `gotta`, `'tis`, `'em` stay whole.
- Hyphenated compounds, numbers with `.` `,` `:` (`3.5`, `1,000`, `10:30`), and `O'Brien`-style names stay whole; a currency sign or `%` is a separate token. A plural possessive apostrophe (`dogs'`) is a separate `'` token.

## 7. Annotation pipeline

All stages are scripted. The raw outputs of the annotators are **not kept** in the repository: the committed result is the data files (`representative/data`, `targeted/data`). The scripts that merge the outputs (`ingest_primary.py`, `finalize.py`) read a work folder (`--workdir`) that holds them, so running them again needs the annotation stages to be run again; because the primary stage is not bit-reproducible (below), that gives an equivalent corpus, not an identical one.

**Guideline and prompts.** `GUIDELINE.md` (version 4) plus 15 short few-shot examples (`prompts/fewshot.txt`) are given to both annotators. `prepare_primary.py` writes the primary task prompts (batches of 60 sentences); the second annotator's full prompt is built by `scripts/annotate_second.py: system_prompt()` (guideline + examples + JSON instructions).

1. **Primary annotator: `gpt-6.1-sol`** (based on the measurements in `quality.md`), run as general-purpose sub-agents of the GitHub Copilot CLI, one agent per batch of 60 sentences (model id passed exactly as given; the platform exposes no snapshot version, temperature or seed, so this stage is not bit-reproducible). Each agent reads the guideline, the examples and its batch and writes `sent_id, idx, form, lemma, UPOS, features` per token. **UPOS, lemma and features come from this annotator; the final tag is always its tag.**
2. **Second, independent annotator and flag: `gemma4:26b`** (Gemma 4, 25.2B parameters, Q4_K_M, Ollama digest `08ae7ec1744bd7f451c4a530afb39d2673ad9d07a8369b8a33a3613b41212a68`, Apache 2.0, confirmed by the license text and `general.license` reported by the Ollama server) on a local Ollama server:
   `POST /api/chat`, `stream: false`, `think: false`, `temperature: 0`, `seed: 42`, `num_ctx: 16384`, JSON-schema constrained output of `"idx:TAG"` strings, 10 sentences per request, 4 requests in flight; responses are validated and failing sentences asked again.
   **It checks UPOS only** and produces no lemma or features. It has run for 10,449 of the 14,980 sentences (all Tatoeba, VOA except 1, all NWS and NPS, 870 of 1,670 USGS sentences, no Gutenberg sentence).
3. **Disagreements and consensus status; no adjudicator.** The final tag is always gpt-6.1-sol's. Where gemma disagrees, the token gets status **`disputed`**; where it agrees, **`unanimous`**; where there is no gemma annotation, **`unchecked`**. The decision is based on measurement: keep-primary beat every adjudication or tie-break policy that was tested (`quality.md`).
4. **Mechanical validation** (`ingest_primary.py`, `finalize.py`, `validate.py`): UPOS in the 17-tag set; collapsed tag equals the mapping of UPOS; lemma non-empty; features well formed, alphabetical and allowed for the UPOS; forms match the tokenized sentence; the tokens concatenate to the text;
   4 to 40 tokens per sentence; no duplicate sentence; every source is listed; every document is in exactly one split.

**Completing the cross-check.** `python scripts/annotate_second.py --sentences build/sentences.tsv --workdir <work folder>` against any Ollama host that serves `gemma4:26b` (same options; `--host URL` to change the host) resumes from its checkpoint and asks only the sentences that have no second annotation; `finalize.py`, `split_data.py`, `validate.py`, `evaluate.py` and `stats.py` then merge it, which needs the primary outputs in the same work folder (so the primary stage is run again first).

## 8. Results: splits, counts and accuracy (representative part)

Split by **document**: bucket = `int(SHA-256(source_id)[:8], 16) % 100`; 0-79 train, 80-89 dev, 90-99 test (`scripts/split_data.py`; the targeted part uses the same bucket with 0-79 train, 80-99 test, section 1a). All sentences of a document are in one split. Each Tatoeba sentence is its own document (near-duplicate Tatoeba sentences can fall in different splits).

| Family | train | dev | test | total | documents | tokens |
|---|---|---|---|---|---|---|
| tatoeba (conversational) | 3,035 | 357 | 358 | 3,750 | 3,750 | 47,433 |
| voa (news) | 3,015 | 349 | 386 | 3,750 | 379 | 55,559 |
| noaa-nws | 631 | 60 | 80 | 771 | 200 | 15,569 |
| usgs | 1,356 | 152 | 162 | 1,670 | 443 | 39,465 |
| nps | 1,042 | 138 | 129 | 1,309 | 363 | 27,316 |
| gutenberg (literary) | 2,911 | 433 | 386 | 3,730 | 175 | 76,299 |
| **all** | **11,990** | **1,489** | **1,501** | **14,980** | **5,310** | **261,641** |

Tokens: train 209,224, dev 26,232, test 26,185 (80.0% / 9.9% / 10.0% of sentences).

Consensus status (tokens): unanimous 157,579 (60.23%), disputed 9,223 (3.53%), **unchecked 94,839 (36.25%)**. Unchecked tokens by family: Gutenberg 76,299 (all), USGS 18,527, VOA 13. Disputed share of the *checked* tokens: 5.53% (conversational 4.83%, news 5.36%, NWS 5.41%, NPS 6.19%, USGS 6.79%).
By split: train unanimous 127,202 / disputed 7,449 / unchecked 74,573; dev 14,887 / 867 / 10,478; test 15,490 / 907 / 9,788. **All literary text is unchecked**, so literary and USGS tokens have no cross-check.

**Accuracy** (measured on 300 sentences of UD-EWT dev, evaluation only; details in `quality.md`): gpt-6.1-sol alone scores **95.05% UPOS and 96.02% collapsed tag** after mapping the reference to the WordSense lexicon conventions (90.63% and 91.60% raw); lemma accuracy 98.36%. On the 10,449 cross-checked sentences the two annotators agree on 94.47% of the UPOS tags (kappa 0.938).

## 9. File format

`representative/data/{train,dev,test}.tsv` and `targeted/data/{train,test}.tsv`: UTF-8, LF. A header block of `# corpus`, `# license`, `# columns`, `# consensus` comment lines, then sentences separated by a blank line. Each sentence is introduced by `# sent_id = ...`, `# source_id = ...` and `# text = ...` (targeted sentences also carry the required `# selection = ...` and `# query = ...` lines between `source_id` and `text`); then one token per line with 7 TAB-separated columns:

| Column | Content |
|---|---|
| `id` | 1-based token index in the sentence |
| `form` | the token as tokenized (section 6) |
| `lemma` | lemma (guideline section 5) |
| `upos` | one of the 17 UPOS tags; always gpt-6.1-sol's tag |
| `collapsed` | WordSense collapsed tag `N R V J B P C I D A T U O` (mapping in `GUIDELINE.md` section 7) |
| `feats` | `Name=Value\|...` (alphabetical) from `Number Tense VerbForm Person Mood Degree PronType Poss`, or`_` |
| `consensus` | `unanimous` (gemma4:26b gave the same UPOS), `disputed` (it did not; the tag is still gpt-6.1-sol's) or `unchecked` (no second annotation exists for the sentence) |

Example:

```text
# sent_id = tatoeba-10113544
# source_id = tatoeba-10113544
# text = Your laugh is contagious.
1 Your your DET D Poss=Yes|PronType=Prs unanimous
2 laugh laugh NOUN N Number=Sing unanimous
3 is be VERB V Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin unanimous
4 contagious contagious ADJ J _ unanimous
5 . . PUNCT O _ unanimous
```

`sent_id` is `<source_id>-s<n>` for documents (n = sentence ordinal in the extracted text) and the source id itself for Tatoeba. `source_id` joins to `sources.tsv`.

**Training guidance.** `disputed` tokens (3.5% of all tokens, 5.5% of the checked ones) are the least reliable: on the UD-EWT dev sample only about 71% of them are right against the reference (75% after the convention map), versus about 92% for `unanimous` tokens raw and 97% mapped (`quality.md`). **Exclude them or down-weight them when training** (for example mask their loss or use weight 0.3).
`unchecked` tokens have no cross-check; their expected accuracy is that of gpt-6.1-sol alone (about 95% UPOS / 96% collapsed against the convention-mapped reference, `quality.md`), a blend of the unanimous and disputed figures; treat them as unanimous-or-disputed in unknown proportion (about 5% would be flagged by the second annotator).
For the cleanest training set use `unanimous` tokens only; for evaluation use only `unanimous` tokens of the dev/test splits, and say so. Literary text is entirely `unchecked` at present. Take tag priors and other usage statistics from `representative/` only; `targeted/` may feed classifier training (its train part) but never statistics, and its test part is a separate challenge set (section 1a).

## 10. Recipe (how to produce an equivalent corpus)

1. **Sources and licence basis** (sections 3 and 4): collect only text that is public domain in the United States or CC0, and record the licence basis and the terms page of every document in `sources.tsv` and `terms.tsv`.
2. **Documents** (`harvest.py`, `build_url_list.py`): discover candidate documents per source, pin them in `sources/urls.tsv`, download them to the git-ignored cache (hashes in `sources/fetch_log.tsv`) and extract clean paragraphs.
3. **Sentence selection** (section 5; `select_sentences.py`, `apply_exclusions.py`): filters, exact-match deduplication, at most 28 sentences per document, four genre groups of 3,750 sentences, seed 20261019; the treebank text is a negative filter only.
4. **Guideline and few-shot** (`GUIDELINE.md` version 4, `prompts/fewshot.txt`): given to both annotators.
5. **Primary annotation** (`prepare_primary.py`, one `gpt-6.1-sol` sub-agent per batch of 60 sentences, `ingest_primary.py`), **second annotator** (`annotate_second.py`, `gemma4:26b`, UPOS only) and **consensus status** (`finalize.py --policy keep-primary`): section 7.
6. **Validation and splits** (`validate.py`, `split_data.py`, `stats.py`): by document, 80/10/10 (representative) and 80/20 (targeted).
7. **Tag counts and tagger evaluation** (the builder, from the repository root): `open-counts`, `evaluate-tagger`.

The scripts live in `tools/open-corpus/scripts/` (below, `$S`), the git-ignored downloads in `tools/open-corpus/cache/` and scratch in `tools/open-corpus/work/` (below, `$W` is `tools/open-corpus/work/annotations`, which holds the annotator outputs; they are not committed). Run the commands from this folder (`corpus/open`), so that the relative paths (`build`, `sources`) resolve; `common.py` knows where the corpus and the cache are. The cache (about 600 MB of downloaded pages and books) is not committed: `harvest.py fetch` downloads it again, and the hashes in `sources.tsv` and `sources/fetch_log.tsv` tell whether a re-download differs.

```text
S=../../tools/open-corpus/scripts
W=../../tools/open-corpus/work/annotations
pip install -r ../../tools/open-corpus/requirements.txt      # lxml==6.1.1, pypdf==6.10.2 (Python 3.13)
python $S/harvest.py terms                      # terms pages -> terms.tsv
python $S/build_url_list.py gutenberg           # candidate discovery (then voa, nws, usgs, nps) -> sources/discovered/*.tsv
python $S/build_url_list.py assemble            # sources/urls.tsv (pinned input list)
python $S/harvest.py fetch                      # sources/urls.tsv -> cache/ (throttled per host)
python $S/harvest.py extract                    # cache/text/, build/documents.tsv, sources/sources_excluded.tsv
python $S/select_sentences.py --pilot build/pilot_sentences.tsv --ud-text <UD-EWT train+dev conllu> --ws-corpus ../../corpus   # build/sentences.tsv, sources.tsv
python $S/prepare_primary.py --sentences build/sentences.tsv --workdir $W --batch-size 60
#   run the prompts of $W/primary/prompt/batchNN.md with gpt-6.1-sol (one sub-agent each)
python $S/ingest_primary.py --sentences build/sentences.tsv --workdir $W
python $S/annotate_second.py --sentences build/sentences.tsv --workdir $W   # needs an Ollama host (--host URL; default http://localhost:11434); resumable
python $S/apply_exclusions.py --sentences build/sentences.tsv --exclusions build/post_exclusions.tsv --out <abs>/build/sentences_final.tsv --sources <abs>/sources.tsv
python $S/finalize.py --sentences build/sentences_final.tsv --workdir $W --policy keep-primary
python $S/split_data.py --sentences build/sentences_final.tsv --workdir $W            # representative/data/
python $S/validate.py                           # both parts; targeted needs # selection and # query
python $S/evaluate.py --workdir $W --sentences build/sentences_final.tsv --out eval/corpus_scores.json
python $S/stats.py                              # representative part; add --part targeted for the targeted part
# a targeted selection: the same annotation steps with its own sentences file (<S>.tsv) and work folder (<W2>), then
python $S/split_data.py --part targeted --sentences <S>.tsv --workdir <W2>
python $S/gutenberg_evidence.py                 # gutenberg-books.md (network: Open Library)
# tag counts for the Basic tagger, and the tagger evaluation (from the repository root):
dotnet run --project tools/WordSense.Builder -c Release -- open-counts          # counts/ from representative/data/train.tsv
dotnet run --project tools/WordSense.Builder -c Release -- evaluate-tagger      # open test split (headline) and the targeted challenge set
# accuracy measurement (needs en_ewt-ud-dev.conllu, SHA-256 verified by the script; keep --out-dir OUTSIDE this folder):
python $S/prepare_ud_eval.py --ud en_ewt-ud-dev.conllu --out-dir ../../tools/open-corpus/work/ud-eval
#   then the same prepare/ingest/annotate_second/finalize steps with --workdir ../../tools/open-corpus/work/ud-eval, and
python $S/evaluate.py --workdir ../../tools/open-corpus/work/ud-eval --gold ../../tools/open-corpus/work/ud-eval/gold.tsv --convention-map --lexicon-data ../../data --out eval/ud_dev_scores.json
# tagger evaluation on the convention-mapped treebank development and test files (evaluation only; the files stay in the git-ignored cache):
python $S/prepare_tagger_eval.py                # work/tagger-eval/ud_dev.tsv and ud_test.tsv
dotnet run --project tools/WordSense.Builder -c Release -- evaluate-tagger --gold tools/open-corpus/work/tagger-eval/ud_dev.tsv
```

Re-running harvest later may fetch changed pages; compare the hashes in `sources.tsv` and `sources/fetch_log.tsv`. The sub-agent stage is not bit-reproducible, so a new annotation run gives an equivalent corpus, not an identical one: the committed data files are the result.

## 11. License (final)

- **Annotations and our selection** (tags, lemmas, features, consensus status, sampling, splits, guideline, few-shot examples, scripts, documentation): **CC0 1.0** (`LICENSE`).
- **Underlying sentences**: public domain in the United States or CC0 per source, as listed in `sources.tsv`. We make no claim on them; outside the United States check your own law (Project Gutenberg texts and the agency pages are public domain in the United States only).
- The guideline uses UPOS tag names only as labels.
- **Treebank**: the Universal Dependencies English-EWT treebank (CC BY-SA 4.0) is an evaluation source only. It is downloaded by hand into the git-ignored `tools/open-corpus/cache/`, never committed or shipped, and nothing here (the counts included) is trained from it.

## 12. Known limitations

- **Cross-check partial**: 36.25% of tokens (all literary text and 800 USGS sentences) are `unchecked`; the `disputed` flag is the only quality signal we have and it is missing there. Treat the corpus as single-annotator for those tokens.
- **Genre gaps**: no social media, forums, e-mail, reviews, transcribed speech or code-switching; Tatoeba stands in for conversation and is written, often translated, and fairly formal. Literary text is English of 1774 to 1930, with archaic forms. Technical text is limited to weather, water science and park-service prose.
  VOA text is simplified learner English.
- **LLM annotation error rate**: against the UD-EWT dev sample (another genre mix, other conventions) gpt-6.1-sol scores 90.63% UPOS raw and 95.05% after the convention map; the mapped figure is the better estimate of the error rate against our own conventions (about 5% of UPOS tags, `quality.md`). No human has reviewed the corpus. Lemmas and features have no second opinion.
- **Possible pretraining exposure** of the LLMs to UD and other treebanks cannot be excluded or measured. Check the conventions in `GUIDELINE.md` before mixing this corpus with UD-trained data.
- **Source artifacts**: Gutenberg text keeps archaic spelling and a few OCR-like splits; some sentences from web pages carry navigation-like phrasing. Gutenberg year entries from Open Library can be approximate (section 3). No manual cleaning was done.
- **Uneven split shares**: document-level hashing gives uneven shares per family and per status in dev and test.
