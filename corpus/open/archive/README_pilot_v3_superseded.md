<!-- markdownlint-disable MD013 MD056 MD060 -->

# Open English POS Corpus (pilot)

An independent, openly licensed English part-of-speech corpus built for the WordSense project as an alternative to UD English-EWT (CC BY-SA 4.0). This folder is the **pilot**: 1,000 sentences, 17,374 tokens, from 391 source documents,
annotated with UPOS, a collapsed WordSense tag, lemma and a few morphological features, with a per-token consensus status. It will be moved into the repository as `corpus/open/`.

**License (final, owner decision of 2026-10-20): our annotations and our selection are released under CC0 1.0** (`LICENSE` is the CC0 1.0 legal code). The underlying sentences are public domain or CC0 per source (section 3); we make no claim on them.
Nothing here is legal advice.

## 1. Contents

| Path | What it is |
| --- | --- |
| `README.md` | This document: sources, licenses, method, formats, reproduction, limitations |
| `LICENSE` | CC0 1.0 Universal legal code (annotations and selection) |
| `GUIDELINE.md` | Our own annotation guideline, version 3, including the UPOS -> WordSense collapsed mapping |
| `guideline-derivation.md` | How the guideline's hard-case list was derived from our own pilot disagreements (two rounds, with gpt-6.1-sol's help) |
| `quality.md` | Measured accuracy (UD-EWT dev sample), agreement, and the evidence for the annotation policy |
| `sources.tsv` | One row per source document used (391): family, title, URL, author, year, license basis (with the quoted statement), terms URL, retrieval date, SHA-256 of the retrieved file and of the extracted text, sentences contributed |
| `gutenberg-books.md` | The 34 Gutenberg books with the recorded publication year and our evidence, for the owner's spot check (`sources/gutenberg_years.tsv` is the machine-readable form) |
| `terms.tsv` | The terms/licence pages read, with status (used or excluded), retrieval date, SHA-256 and the key sentence found |
| `sources/urls.tsv`, `sources/sources_excluded.tsv`, `sources/fetch_log.tsv` | The pinned input list (230 candidate pages), the candidates rejected and why, and the retrieval date and SHA-256 of every raw download |
| `data/{train,dev,test}.tsv` | The annotated pilot (format in section 9) |
| `annotations/` | Raw outputs of every annotation stage (prompts, inputs, primary outputs, second-annotator log) |
| `build/` | `sentences.tsv` (the 1,000 selected sentences with tokens), `dropped_duplicates.tsv`, `documents.tsv` |
| `eval/` | `ud_dev_sample_ids.txt` (ids only), `ud_dev_scores.json`, `pilot_scores.json`; `eval/v1`, `eval/v2` hold the scores of earlier drafts |
| `prompts/` | `fewshot.txt` (examples from our own first pilot draw) and `fewshot_ids.txt` |
| `archive/` | Superseded drafts (guideline v1 and v2, few-shot v1) and the round-2 evidence behind guideline v3 (kept for the record) |
| `scripts/` | All code (Python, standard library plus `lxml` and `pypdf`; `requirements.txt`) |
| `cache/`, `work/` | Not part of the corpus (raw third-party downloads and scratch; git-ignored; do not move) |

## 2. Independence statement

This is our position, stated as precisely as we can; it is not legal advice.

1. **No UD text, annotations or guideline text were used as input.** No sentence comes from a Universal Dependencies treebank (an exact-match check of the current 1,000 sentences against the text of UD-EWT dev and train found no overlap), no UD tag or lemma was shown to any annotator,
   and no UD documentation was given to any model or copied into `GUIDELINE.md`.
2. **The guideline was written independently, and its hard-case list is built from our own pilot data only.** `GUIDELINE.md` (version 3) and the few-shot examples were authored for this corpus in our own words with our own examples; it uses the 17 UPOS tag *names* (facts) and the names of eight morphological features.
   Section 4 was derived from the disagreements between our annotators on our own pilot sentences, in two rounds, with gpt-6.1-sol helping to analyze the second round (`guideline-derivation.md`). The few-shot examples are 20 sentences of our own first pilot draw, annotated by hand.
3. **An earlier draft was UD-informed and is superseded.** Version 1 of the guideline had a hard-case list that was partly chosen from error categories (not text) found in an exploratory accuracy test of LLM taggers against UD-EWT dev, as listed by the project owner. That draft is superseded: no published annotation was produced with it,
   and the hard-case list of version 3 does not use it. We cannot claim zero influence: the first pilot draw was tagged with version 1 and its disagreements were the data for round 1 (version 2); version 2 and version 3 reverse several version 1 choices and add many rules that version 1 did not have, but the lineage exists and is documented.
   The drafts are kept in `archive/` for transparency. The guideline's authors are LLMs, which may know treebank conventions from pretraining.
4. **Sources and licenses are listed per document** in `sources.tsv` (license basis, quoted statement, terms URL, retrieval date, SHA-256).
5. **All annotations were produced by the listed models from the listed prompts** (section 7): gpt-6.1-sol (primary) and gemma4:26b (second annotator, flag only). No tag was edited by hand. Mechanical steps only: validation, removal of features not allowed on the final UPOS, re-asking sentences that failed validation, and replacing two kinds of unsuitable sentences (section 5).
6. **UD-EWT dev was used only to measure annotation accuracy** (300 sentences, ids in `eval/ud_dev_sample_ids.txt`), never as input to annotation, prompting, tuning or training. UD test was never used. No UD text or tags are stored in this folder.
7. **Possible pretraining exposure.** The LLMs may have been exposed to public treebanks, including UD, during pretraining; that cannot be ruled out. Our position is that this does not make the corpus a derivative of UD: the sentences are our own selections from other sources, and every tag, lemma and feature was generated per sentence by the models from our guideline and prompts, as facts about those sentences
   (a part of speech is a fact about a word in context, not a copy of any treebank's expression). We cannot prove the models never reproduced a memorized annotation. Our conventions differ from UD's in several deliberate ways (for example possessive words before a noun are DET, `be` is always AUX, particles of phrasal verbs are ADV), which is visible in the accuracy against UD (`quality.md`, about 93%).

## 3. Sources and license basis

Only agencies whose **own site carries an explicit public-domain statement** are used (owner rule). Terms checked and retrieved on **2026-10-19** (hash and key sentence per page in `terms.tsv`). Raw downloads: Gutenberg texts and the Tatoeba export were reused from a local cache (retrieval date = file date, recorded in `sources/fetch_log.tsv`);
all other pages were downloaded on 2026-10-19 or 2026-10-20.

| Family | Pilot sentences | Documents | Evidence | Statement (exact quote) and URL |
| --- | --- | --- | --- | --- |
| Project Gutenberg | 250 | 34 books | **verified** | US public domain: first published 1930 or earlier (years and evidence per book in `gutenberg-books.md`); each landing page states "Public domain in the USA". Gutenberg license: "If you strip the Project Gutenberg license and all references to Project Gutenberg from the text, you are left with a text unrestricted by U.S. intellectual property law" <https://www.gutenberg.org/policy/license.html>; "The vast majority of Project Gutenberg eBooks are in the public domain in the US" <https://www.gutenberg.org/policy/permission.html>. |
| VOA Learning English | 250 | 41 articles | **verified** (VOA-only articles) | "All text, audio and video material produced exclusively by the Voice of America is in the public domain" <https://www.voanews.com/p/5338.html>; AP, AFP and Reuters material is licensed and excluded. |
| NOAA National Weather Service (weather.gov, NOAA JetStream) | 84 | 23 pages | **verified** | "The information on National Weather Service (NWS) Web pages are in the public domain, unless specifically noted otherwise" <https://www.weather.gov/disclaimer>. |
| NOAA National Ocean Service (Ocean Facts) | 39 | 10 pages | **statement is weaker** | "Information presented on this website is considered public information and may be distributed or copied, unless specifically noted otherwise" <https://oceanservice.noaa.gov/disclaimer.html> (not literally "public domain"; 17 U.S.C. 105 applies to agency-authored text; kept by owner decision). |
| USGS | 76 | 20 pages | **verified** | "USGS-authored or produced data and information are considered to be in the U.S. Public Domain" <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits> (the page also says some non-USGS images and graphics are used with permission; pages with copyright markers are excluded; text only). |
| US National Park Service | 51 | 13 pages | **verified** | "material created by the National Park Service and presented on this website, unless otherwise indicated, is generally considered in the public domain" <https://www.nps.gov/aboutus/disclaimer.htm> (third-party and trademarked material is excluded by the same page; pages with copyright markers are excluded). |
| Tatoeba | 250 | 250 sentences | **verified** via official CC0 export | Only sentences in the official `sentences_CC0` export <https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2>; "A part of our sentences are also available under CC0 1.0" <https://tatoeba.org/en/downloads>. No contributor names in the export. No per-sentence page check was possible with a script. |
| **Total** | **1,000** | **391** | | |

The US term rule used for Gutenberg is 95 years from publication for pre-1978 works (copyright.gov FAQ in `terms.tsv`); works published in 1930 or earlier are in the US public domain in 2026 (the latest year used is 1925). 17 U.S.C. 105(a) (no copyright protection for works of the US Government) is the statutory basis for the agency pages;
105(b) (a recent provision for certain works by employees of specified defense and intelligence institutions) does not apply to these agencies. NWS asks that you do not claim copyright in its information: the CC0 dedication covers our annotations and selection only, not the underlying sentences.

**Project Gutenberg trademark.** We use only the book text between the START and END markers (header, footer, license, transcriber notes and any paragraph mentioning Gutenberg, an e-book or a transcriber are removed). `sources.tsv` credits the origin by link only, which the Gutenberg license treats as outside trademark use.
Gutenberg texts are public domain in the US only; users elsewhere must check their own law.

## 4. Exclusions and why

- **FAA and NASA** (dropped on 2026-10-20 by owner decision; no sentence of them is in this corpus): the FAA site carries no copyright or public-domain statement (statute only); NASA says its content is "generally not subject to copyright" and marks occasional third-party material, which is not an explicit public-domain statement. Their terms rows stay in `terms.tsv` with status `excluded`.
- **CDC**: excluded. Its reuse page attaches conditions beyond 17 U.S.C. 105 (attribution, disclaimer, no change to substantive content, a free-availability statement), the site blocks scripted access (HTTP 403), and it warns of contractor and licensed material. **MedlinePlus (A.D.A.M. encyclopedia)**: copyrighted.
- **Wikipedia, Wikinews, Wiktionary, any CC BY-SA text, news sites, social media, Common Crawl derivatives**: excluded by policy. **All Universal Dependencies text, tags and documentation**: excluded (independence). **Tatoeba CC BY sentences**: excluded; only CC0 sentences are used.
- **VOA**: 126 candidate pages were fetched; 85 rejected: 77 contain an AP, Reuters, AFP or copyright marker anywhere in the article body (most "As It Is" and health/science stories are adapted from AP or AFP reports) and 8 have no article body. The 41 kept articles are staff-written. Video captions, photo credits and glossaries are not used.
- **USGS/NPS/NWS/NOS**: only prose paragraphs are used; pages whose text contains a copyright marker are rejected (none were); one NPS page returned an error (`sources/sources_excluded.tsv`).
- **Gutenberg**: translations (translator date unverified) are excluded by rule; the books used are original English works. Also excluded: images, audio, video, logos, and any page whose license basis cannot be stated in one sentence.

## 5. Selection and sampling

Pilot mix (250 sentences each): conversational (Tatoeba), news (VOA Learning English), technical/informational (NWS 84, NOS 39, USGS 76, NPS 51), literary/historical (Gutenberg). Deterministic, seed **20261019** (`scripts/common.py`); every random choice derives from it.

1. `harvest.py` extracts clean paragraphs per document (HTML through `lxml`; VOA from the article body; Gutenberg between the START and END markers).
2. `select_sentences.py` splits paragraphs into sentences (abbreviation-aware regex), normalizes typography (curly quotes to ASCII, dashes to `--`), tokenizes, and keeps sentences that pass: 4 to 40 tokens; start with a capital letter; end with `.`, `?` or `!`; balanced quotes and brackets; no URL, markup character or replacement character;
   at most 3 numeric tokens and at most 20% digit characters (no tables or number lists); at least 60% alphabetic tokens; not mostly upper case; not a caption; no long title-case runs. Questions, imperatives and short dialogue lines are kept. The 20 few-shot sentences are excluded from the pool.
   A sentence repeated in several documents of a group is kept once.
3. Per group the quota is spread evenly over the documents (`ceil(250 / documents)`, seeded sampling, then seeded trimming or top-up to exactly 250). Tatoeba: every sentence is its own document, drawn uniformly from the deduplicated CC0 English sentences that pass the filters.
4. **Repair stage.** Sentences that are duplicates of another selected sentence, or that contain a token with punctuation fused to letters (a missing space in the source, which no annotator could align), are dropped (`build/dropped_duplicates.tsv`) and replaced by a seeded draw from the same document (new uid `P1001`); in this draw one sentence was replaced
   (`P0551`: the second annotator could not align its tokens after three asks).

Average sentence length: 17.4 tokens (conversational 13.3, news 14.7, technical 20.6, literary 20.9).

## 6. Tokenization

`scripts/common.py: tokenize`, rule-based; the tokens always concatenate back to the text without spaces (validated):

- Typographic quotes and apostrophes are normalized to ASCII `"` and `'`; `--`, an em dash or an en dash becomes the token `--`.
- Punctuation and brackets are separate tokens; `...` is one token. A trailing period stays attached to a known abbreviation (`Mr.`, `Dr.`, `St.`, `etc.`, `U.S.`, `a.m.`, and a single capital initial such as `J.`, except at the sentence end). `No.` is in the abbreviation list, so an answer "No." stays one token.
- Clitics are split off: `n't` (`do` + `n't`; `can't` -> `ca` + `n't`, `won't` -> `wo` + `n't`, `shan't` -> `sha` + `n't`, `ain't` -> `ai` + `n't`), `'s`, `'re`, `'ve`, `'ll`, `'d`, `'m`; `cannot` -> `can` + `not`. `gonna`, `wanna`, `gotta`, `'tis`, `'em` stay whole.
- Hyphenated compounds, numbers with `.` `,` `:` (`3.5`, `1,000`, `10:30`), and `O'Brien`-style names stay whole; a currency sign or `%` is a separate token. A plural possessive apostrophe (`dogs'`) is a separate `'` token.

## 7. Annotation pipeline

All stages are scripted; model outputs are archived under `annotations/` (the UD evaluation run was kept outside this folder).

**Guideline and prompts.** `GUIDELINE.md` plus 15 short few-shot examples (`prompts/fewshot.txt`) are given to both annotators. The primary task prompts are generated into `annotations/primary/prompt/batchNN.md`; the second annotator's full prompt is built by `scripts/annotate_second.py: system_prompt()` (guideline + examples + JSON instructions).

1. **Primary annotator: `gpt-6.1-sol`** (owner decision of 2026-10-20, based on measurements in `quality.md`), run as general-purpose sub-agents of the GitHub Copilot CLI (model id passed exactly as given; the platform exposes no snapshot version, temperature or seed, so this stage is not bit-reproducible).
   20 batches of up to 50 sentences were annotated in parallel, each agent reading the guideline, the examples and its batch and writing `sent_id, idx, form, lemma, UPOS, features` per token. All 1,000 pilot sentences validated on the first try (plus one replacement sentence). **UPOS, lemma and features come from this annotator; the final tag is always its tag.**
   (claude-sonnet-5.5 was used in earlier drafts only and is no longer an annotator.)
2. **Second, independent annotator and flag: `gemma4:26b`** (Gemma 4, 25.2B parameters, Q4_K_M, Ollama digest `08ae7ec1744bd7f451c4a530afb39d2673ad9d07a8369b8a33a3613b41212a68`, Apache 2.0, confirmed by the license text and `general.license` reported by the Ollama server) on a local Ollama server:
   `POST /api/chat`, `stream: false`, `think: false`, `temperature: 0`, `seed: 42`, `num_ctx: 16384`, JSON-schema constrained output of `"idx:TAG"` strings, **10 sentences per request, 4 requests in flight**. Responses are validated (sentence ids, token count, index order, tag set) and failing sentences asked again once.
   In the pilot, 100 requests plus 4 re-asks were needed; one sentence still failed (see section 5). `gemma4:e4b` was not used. **It checks UPOS only** and produces no lemma or features.
3. **No adjudicator.** The final tag is always gpt-6.1-sol's. Where gemma disagrees, the token gets consensus status **`disputed`** (4.54% of pilot tokens); otherwise **`unanimous`** (95.46%). The decision is based on measurement: keep-primary beat every adjudication or tie-break policy that was tested (`quality.md`).
   (The scripts `adjudicate.py`, `adjudicate_ollama.py` and `compare_judges.py` belong to that earlier experiment and are not part of the final pipeline.)
4. **Mechanical validation** (`ingest_primary.py`, `finalize.py`, `validate.py`): UPOS in the 17-tag set; collapsed tag equals the mapping of UPOS; lemma non-empty; features well formed, alphabetical and allowed for the UPOS (none had to be removed in this run); forms match the tokenized sentence; the tokens concatenate to the text;
   4 to 40 tokens per sentence; no duplicate sentence; every source is listed; every document is in exactly one split.

Wall clock for the final pipeline on the pilot: sol batches (20 in parallel) a few minutes; the second annotator 25.2 min for the pilot (1,000 sentences) and 9.0 min for the 300 evaluation sentences (the two runs shared the server).

## 8. Splits and counts

Split by **document**: bucket = `int(SHA-256(source_id)[:8], 16) % 100`; 0-79 train, 80-89 dev, 90-99 test (`scripts/split_data.py`). All sentences of a document are in one split. Each Tatoeba sentence is its own document (near-duplicate Tatoeba sentences can fall in different splits).

| Family | train | dev | test | total | documents | tokens |
| --- | --- | --- | --- | --- | --- | --- |
| tatoeba (conversational) | 204 | 25 | 21 | 250 | 250 | 3,323 |
| voa (news) | 190 | 35 | 25 | 250 | 41 | 3,676 |
| noaa-nws | 59 | 8 | 17 | 84 | 23 | 1,623 |
| noaa-nos | 23 | 8 | 8 | 39 | 10 | 860 |
| usgs | 64 | 8 | 4 | 76 | 20 | 1,598 |
| nps | 43 | 4 | 4 | 51 | 13 | 1,058 |
| gutenberg (literary) | 205 | 16 | 29 | 250 | 34 | 5,236 |
| **all** | **788** | **104** | **108** | **1,000** | **391** | **17,374** |

Tokens: train 13,843, dev 1,728, test 1,803 (about 79% / 10% / 11% of sentences). The hash rule gives uneven shares per family at this size (for example 4 USGS and 4 NPS sentences in test).

## 9. File format

`data/{train,dev,test}.tsv`: UTF-8, LF. A header block of `# corpus`, `# columns`, `# consensus` comment lines, then sentences separated by a blank line. Each sentence is introduced by `# sent_id = ...`, `# source_id = ...` and `# text = ...`; then one token per line with 7 TAB-separated columns:

| Column | Content |
| --- | --- |
| `id` | 1-based token index in the sentence |
| `form` | the token as tokenized (section 6) |
| `lemma` | lemma (guideline section 5) |
| `upos` | one of the 17 UPOS tags; always gpt-6.1-sol's tag |
| `collapsed` | WordSense collapsed tag `N R V J B P C I D A T U O` (mapping in `GUIDELINE.md` section 7) |
| `feats` | `Name=Value | ...` (alphabetical) from `Number Tense VerbForm Person Mood Degree PronType Poss`, or`_` |
| `consensus` | `unanimous` (gemma4:26b gave the same UPOS) or `disputed` (it did not; the tag is still gpt-6.1-sol's) |

Example:

```text
# sent_id = tatoeba-10113544
# source_id = tatoeba-10113544
# text = Your laugh is contagious.
1 Your your DET D Poss=Yes|PronType=Prs unanimous
2 laugh laugh NOUN N Number=Sing unanimous
3 is be AUX A Mood=Ind|Number=Sing|Person=3|Tense=Pres|VerbForm=Fin unanimous
4 contagious contagious ADJ J _ unanimous
5 . . PUNCT O _ unanimous
```

`sent_id` is `<source_id>-s<n>` for documents (n = sentence ordinal in the extracted text) and the source id itself for Tatoeba. `source_id` joins to `sources.tsv`. For training, `disputed` tokens (4.5%) are the least reliable (about 70% correct against a reference with other conventions, `quality.md`); drop or down-weight them.

## 10. Reproduction

```text
pip install -r requirements.txt                      # lxml==6.1.1, pypdf==6.10.2 (Python 3.13)
python scripts/harvest.py terms                      # terms pages -> terms.tsv
python scripts/update_url_list.py                    # recorded change of the input list (owner rule: explicit public-domain statements only)
python scripts/harvest.py fetch                      # sources/urls.tsv -> cache/ (hashes in sources/fetch_log.tsv)
python scripts/harvest.py extract                    # cache/text/, build/documents.tsv, sources/sources_excluded.tsv
python scripts/select_sentences.py                   # build/sentences.tsv, sources.tsv (seed 20261019)
python scripts/gutenberg_evidence.py                 # gutenberg-books.md
python scripts/prepare_primary.py --sentences build/sentences.tsv --workdir annotations
#   run the prompts annotations/primary/prompt/batchNN.md with gpt-6.1-sol (one sub-agent each)
python scripts/ingest_primary.py --sentences build/sentences.tsv --workdir annotations
python scripts/annotate_second.py --sentences build/sentences.tsv --workdir annotations   # needs the Ollama host
python scripts/finalize.py --sentences build/sentences.tsv --workdir annotations --policy keep-primary
python scripts/split_data.py --sentences build/sentences.tsv --workdir annotations
python scripts/validate.py
python scripts/evaluate.py --workdir annotations --sentences build/sentences.tsv --out eval/pilot_scores.json
python scripts/stats.py
# accuracy measurement (needs en_ewt-ud-dev.conllu, SHA-256 verified by the script; keep --out-dir OUTSIDE this folder):
python scripts/prepare_ud_eval.py --ud en_ewt-ud-dev.conllu --out-dir ../ud-eval-work
#   then the same prepare/ingest/annotate_second/finalize steps with --workdir ../ud-eval-work, and
python scripts/evaluate.py --workdir ../ud-eval-work --gold ../ud-eval-work/gold.tsv --out eval/ud_dev_scores.json
```

Re-running harvest later may fetch changed pages; compare the hashes in `sources.tsv` and `sources/fetch_log.tsv`. The sub-agent stage is not bit-reproducible; reuse the archived raw outputs in `annotations/` to rebuild the data files exactly.

## 11. License (final)

- **Annotations and our selection** (tags, lemmas, features, consensus status, sampling, splits, guideline, few-shot examples, scripts, documentation): **CC0 1.0** (`LICENSE`).
- **Underlying sentences**: public domain or CC0 per source, as listed in `sources.tsv`. We make no claim on them.
- The guideline uses UPOS tag names only as labels.

## 12. Known limitations

- **Genre gaps**: no social media, forums, e-mail, reviews, transcribed speech or code-switching; Tatoeba stands in for conversation and is written, often translated, and fairly formal. Literary text is English of 1774 to 1925, with archaic forms. Technical text is limited to weather, ocean science, water science and park-service prose.
  VOA text is simplified learner English. The government-source filter removed aviation and space prose, so the technical group is more descriptive and less instructional than first planned.
- **LLM annotation error rate**: about 7% of UPOS tags differ from the UD sample's reference (93.08%), part of it by design (section 2, point 7). On the 4.5% `disputed` tokens only about 70% match. No human has reviewed the pilot. Lemmas and features have no second opinion.
- **Possible pretraining exposure** of the LLMs to UD and other treebanks cannot be excluded or measured. The conventions differ from UD's in the ways documented in `quality.md` and `GUIDELINE.md`; check them before mixing this corpus with UD-trained data.
- **Source artifacts**: Gutenberg text keeps archaic spelling and a few OCR-like splits (`p ennyworth`); some sentences from web pages carry navigation-like phrasing. No manual cleaning was done.
- **Small test split**: 108 sentences; some families have very few test sentences.
- **Provenance caveat on lineage**: see section 2, point 3.

## 13. Open questions for the owner

1. NOS (Ocean Facts) says "public information ... may be distributed or copied", not literally "public domain": keep it (as decided) or replace it with another source that has the explicit words?
2. Possessive words before a noun are DET in our guideline, which costs about 1.4 points against a reference that tags them PRON; keep for independence, or switch (one-line change)?
3. Gutenberg publication years: `gutenberg-books.md` lists the evidence; the weakest entry is ebook 7280 (Burroughs, *My Boyhood*), see its row. Spot-check or drop it.
4. Scale-up: a human-reviewed subset (a real gold sample) and more genres (forums, e-mail, speech) would need sources that clear the same license bar.
