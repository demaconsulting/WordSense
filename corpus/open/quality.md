<!-- markdownlint-disable MD013 MD056 MD060 -->

# Annotation Quality

Measured on 2026-10-20. Numbers come from `tools/open-corpus/scripts/evaluate.py` (`eval/ud_dev_scores.json` for accuracy, `eval/corpus_scores.json` for agreement on the corpus) and, for the history, from the archived score files under `eval/v1/` and `eval/v2/`.

**Headline.** Against UD English-EWT dev (300 sentences, 3,597 tokens), gpt-6.1-sol alone with guideline v4 scores **95.05% UPOS / 96.02% collapsed after mapping the reference to the WordSense lexicon conventions** (raw: 90.63% / 91.60%).
The mapped number is the headline because UD's conventions differ from the lexicon's in four documented ways (section 3). **The second-annotator cross-check is pending for 36.25% of the corpus** (section 6); the sol-alone figures apply to every token, including `unchecked` ones.

## 0. Scope: which part these numbers describe

All figures in this file describe the **`representative/` part** (random sample) and the UD-EWT dev measurement of the pipeline. They are the only numbers to quote as "real text" quality.
The **`targeted/` part** (deliberately chosen sentences; so far the 32 simplified-technical-English sentences) never enters them: its test split is a **challenge set** that must be reported in its own table, labeled as such, and never merged into the headline or into any usage statistic (`README.md` section 1a).
`evaluate.py` and `stats.py` (in `tools/open-corpus/scripts/`) read the representative part by default (`stats.py --part targeted` reports the targeted part separately); the "Challenge set" section at the end of this page reports the challenge set with its own selection key and scores.

## 1. Final pipeline

- **Primary annotator: gpt-6.1-sol** (UPOS, lemma, features), general-purpose sub-agents of the GitHub Copilot CLI, batches of 60 sentences in parallel. The final tag is always its tag.
- **Second annotator: gemma4:26b** (Ollama, UPOS only, temperature 0, seed 42). It is used only as a **flag**: a token where it disagrees gets consensus status `disputed`, where it agrees `unanimous`. For sentences it has not annotated yet the status is **`unchecked`**. There is **no adjudicator**.
- Guideline: `GUIDELINE.md` version 4 (hard cases rebuilt from our own pilot disagreements, `guideline-derivation.md`; conventions checked against the WordSense lexicon).

### Why no adjudicator and why gpt-6.1-sol is the primary

Measured earlier (outside this folder, `llmtag/results-sol.md`, an exploratory measurement with an earlier guideline on a different 500-sentence sample of UD English-EWT dev; shown here only as the reason for the decision):

| Annotator | UPOS | Collapsed |
| --- | --- | --- |
| gpt-6.1-sol | 96.41% | 97.25% |
| claude-sonnet-5.5 | 95.49% | 96.31% |
| gpt-5.6-luna | 92.54% | 93.51% |
| gemma4:26b | 90.30% | 92.12% |

No tie-break or majority policy beat gpt-6.1-sol alone on that sample (sol alone 96.41 / 97.25; sol/sonnet with a gemma tie-break 96.04 / 96.83; luna+sonnet agree else sol 95.78 / 96.54; majority of sol, sonnet, luna 95.78 / 96.54).

Our own earlier measurements in this folder agree: **keep-primary beat every adjudication policy that was tested.** On the 300-sentence UD sample, with claude-sonnet-5.5 as primary and earlier guidelines (raw accuracy, no convention map):

| Policy (UD sample, 3,597 tokens) | UPOS | Collapsed |
| --- | --- | --- |
| Guideline v1: keep primary (sonnet) | 95.47% | 96.36% |
| Guideline v1: blind gpt-5.6-luna adjudication | 94.88% | 96.00% |
| Guideline v2: keep primary (sonnet) | 93.02% | 94.25% |
| Guideline v2: blind gpt-5.6-luna adjudication | 92.72% | 94.02% |
| Guideline v2: blind qwen3.8:27b (Apache-2.0 per Ollama) adjudication | 92.72% | 93.94% |
| Guideline v2: blind gemma4:26b adjudication (it picked its own tag 92 times of 214) | 92.13% | 93.41% |

(The gemma and qwen judge runs were completed before the owner decided to skip that experiment; they are reported because they point the same way. Files: `eval/v1/`, `eval/v2/`.)
The judges were right less often than the primary on the disputed tokens (v2: primary 71.0%, luna 65.9%, qwen 65.9%, gemma 56.1%). The `disputed` status therefore keeps the primary tag and marks the token instead of overruling it.

## 2. Method of the measurement

The final pipeline (sol primary, gemma flag, guideline v4, the same few-shot examples) was run on a fixed sample of **300 sentences (3,597 tokens) from UD English-EWT dev** (pinned commit `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b`; `en_ewt-ud-dev.conllu` SHA-256
`39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8`, verified by the script before use). The tokens given to the annotators are the UD word tokens so that comparison is token by token; UD tags were never shown to any annotator.
The sample uses seed 3003 over dev sentences of at most 40 tokens, excluding the 500 sentences of an earlier exploratory test; ids are in `eval/ud_dev_sample_ids.txt` (ids only). **UD-EWT dev was used solely to measure annotation accuracy; it was never
input to annotation (no tags, text or documentation of any UD treebank was given to any annotator, judge or prompt) and never used for training or tuning. UD test was never used.** No UD text is stored in this folder; the evaluation inputs and outputs were kept outside it.
The sample was not re-drawn or re-run for the scale-up: the guideline, few-shot examples and models are the ones used for the corpus.

## 3. Results on the UD English-EWT dev sample

### The convention map (UD -> WordSense lexicon), evaluation only

The corpus follows WordSense's own lexicon (`GUIDELINE.md` section 9). Before comparing, a UD reference tag that follows a different convention is mapped to ours (`tools/open-corpus/scripts/convention_map.py`; the lexicon is read from the repository's `data/form` tables, read-only):

| Rule | UD reference | Mapped to | Basis in the lexicon | Reference tokens remapped |
| --- | --- | --- | --- | --- |
| possessive determiners | PRON, Poss=Yes, relation `nmod:poss` (`my`, `his`, `their`, `whose`) | DET | determiner-only analyses (WordSense's own running-text evaluation maps possessive pronouns to determiners too) | 48 |
| copula `be` | AUX, lemma `be`, relation `cop` | VERB | the verb `be` carries the copula, existence, location and identity senses; AUX is the helper (progressive, passive) | 93 |
| particles of phrasal verbs | ADP, relation `compound:prt` | ADV | adverb analyses of `up`, `out`, `off`, `down` carry directional/completive senses | 7 |
| prepositions before gerunds / no conjunction analysis | SCONJ, and the lexicon has no conjunction analysis for the word or the relation is `mark` with a gerund head | ADP | `of`, `with`, `about`, `by` have no conjunction analysis | 13 |

161 reference tokens (4.5%) are remapped. Nothing is remapped in the corpus; this changes only the scoring.

### Accuracy (final tag = gpt-6.1-sol)

| Measure | UPOS | Collapsed (12-tag) |
| --- | --- | --- |
| **Convention-mapped reference (headline)** | **95.05%** | **96.02%** |
| Raw UD reference | 90.63% | 91.60% |
| Second annotator alone (gemma4:26b), mapped / raw | 91.21% / 87.43% | - / 89.02% |

Lemma accuracy of the primary annotator (case-insensitive match with the reference lemma): 98.36%.

### By consensus status (applies to the checked tokens)

| Status | Tokens | Share | UPOS mapped | Collapsed mapped | UPOS raw | Collapsed raw |
| --- | --- | --- | --- | --- | --- | --- |
| unanimous (gemma agrees with sol) | 3,344 | 92.97% | 96.56% | 97.34% | 92.14% | 92.91% |
| disputed (gemma differs) | 253 | 7.03% | 75.10% | 78.66% | 70.75% | 74.31% |

On the 253 disputed tokens the final (sol) tag matched the mapped reference 190 times (75.1%), gemma's tag 52 times (20.6%), neither 11 times: keeping sol's tag was right almost four times as often as gemma's (raw: 179 / 64 / 10).
There is no `unchecked` row: every token of the evaluation sample had a second annotation. For an `unchecked` token the expected accuracy is the sol-alone figure above.

### What the remaining raw errors are

Raw confusions (reference tag -> our tag): AUX->VERB 101, PRON->DET 50, VERB->ADJ 20, PROPN->NOUN 15, PROPN->X 15 (URLs and e-mail addresses are X for us), SCONJ->ADP 14, ADP->ADV 13, DET->PRON 10, ADJ->PROPN 9, NOUN->PROPN 7, PUNCT->SYM 6, ADJ->NOUN 6.
After the map: VERB->ADJ 20 (participles), PROPN->NOUN 15, PROPN->X 15, DET->PRON 10, ADJ->PROPN 9, AUX->VERB 8 (helper `be` that the reference treats as copula and vice versa), NOUN->PROPN 7, ADP->ADV 6, PUNCT->SYM 6, ADJ->NOUN 6. These are genuine judgment calls and genre differences (EWT is weblogs, e-mail, reviews, answers, newsgroups; the corpus is other genres), not convention differences.

## 4. History: guideline versions on the same sample (raw accuracy, no map)

| Run | Primary | Guideline | Primary UPOS / collapsed | Agreement with gemma (UPOS) |
| --- | --- | --- | --- | --- |
| first draft | claude-sonnet-5.5 | v1 (partly UD-informed, superseded) | 95.47% / 96.36% | 92.97% |
| intermediate | claude-sonnet-5.5 | v2 (round 1 of our own pilot data) | 93.02% / 94.25% | 94.05% |
| pilot | gpt-6.1-sol | v3 (round 2, with sol) | 93.08% / 94.16% | 93.91% |
| **final** | **gpt-6.1-sol** | **v4 (`be` flipped to the lexicon convention)** | **90.63% / 91.60% raw; 95.05% / 96.02% mapped** | **92.97%** |

The drop from v1 to v2 is the intended loss of closeness to UD's conventions (independence was the goal). The drop from v3 to v4 raw is the `be` flip: 93 copula tokens that v3 tagged AUX (as UD does) are VERB in v4; the convention map removes that difference, which is why the mapped figure is the one to compare against earlier work.

## 5. Why not simply compare raw numbers across versions

Every convention difference counts as an error in the raw figure, so the raw numbers are a *lower bound* on the accuracy against our own guideline, and they change whenever a convention changes (as v4 did on purpose). The mapped figure holds the reference to our conventions for the four checked differences; other convention differences are not mapped and remain in the error count.

## 6. Inter-annotator agreement on the corpus

Only the 10,449 sentences with a second annotation can be compared (166,802 tokens: all Tatoeba, all VOA but one sentence, all NWS and NPS, 870 of 1,670 USGS sentences; no Gutenberg sentence yet).

| Data | Tokens compared | UPOS agreement | UPOS kappa | Collapsed agreement | Collapsed kappa |
| --- | --- | --- | --- | --- | --- |
| **Corpus, checked sentences** | 166,802 | **94.47%** | 0.9379 | 95.00% | 0.9424 |
| UD sample (300 sentences) | 3,597 | 92.97% | 0.9220 | 93.97% | 0.9313 |

Agreement by group (UPOS, checked tokens): conversational 95.17%, news 94.64%, technical 93.80% (63,823 of 82,350 tokens checked), literary: not checked.
Status over the whole corpus (261,641 tokens): **unanimous 157,579 (60.23%), disputed 9,223 (3.53%), unchecked 94,839 (36.25%)**; disputed share of the checked tokens 5.53% (conversational 4.83%, news 5.36%, NWS 5.41%, NPS 6.19%, USGS 6.79%).
The disputed share is lower on the corpus (5.5%) than on the UD sample (7.0%): the EWT genres are harder and the reference conventions differ.

## 7. Indicative accuracy of the corpus

If the corpus behaves like the UD sample against our own conventions, about 95% of UPOS tags are right overall: roughly 96.5% of the unanimous tokens and 75% of the disputed tokens, and `unchecked` tokens fall in between (the sol-alone figure, 95%). The 5.5% of checked tokens that are `disputed` hold a large share of the errors (about a quarter of the disputed tokens are wrong versus about 3.4% of the unanimous ones).
This is an extrapolation from a different genre mix, not a measurement on the corpus; **no human has checked the corpus tags**. Lemmas and features were produced by the primary annotator alone and have no second opinion (lemma accuracy 98.36% on the UD sample).

## 8. Caveats

- **The second-annotator cross-check is pending** for 36.25% of tokens (all literary text and 800 USGS sentences); see `README.md` section 7 for how to complete it. The accuracy of sol alone on the UD sample is the best estimate for those tokens.
- Annotators are LLMs; errors correlate (all models may have seen public treebanks in pretraining), and agreement cannot detect an error all of them share.
- The second annotator checks UPOS only. A lemma that depends on the tag (a noun/verb pair such as `saw`) is not recomputed.
- The sub-agent stages cannot set a temperature or seed, so the primary stage is not bit-reproducible; raw outputs are kept in `annotations/` and the ingest scripts are deterministic.
- The measurement sample also informed the choice between policies (keep-primary vs judges), and the `be` flip was decided from the lexicon, not from this sample; the differences between policies are a few tokens and should not be over-read.

## Challenge set (targeted part, test split)

The 32 simplified-technical-English sentences (`targeted/data/test.tsv`, selection `genre=technical-instructions`, query `written:ste-probe`; `README.md` section 1b) are reported here and nowhere else. They are a smoke test: 272 tokens written by the project, with two annotators agreeing on every part of speech, and not running text. The tag accuracy of the Basic classifier on them (`evaluate-tagger`, 222 scored tokens, 138 of them ambiguous in the lexicon; punctuation, symbols and numbers are excluded): **98.65% (97.83% on the ambiguous tokens)** with the table trained on the counts of the representative part of this corpus, against 95.95% (93.48%) for the table trained on the UD-EWT counts that it replaces.

## Use as training data of the Basic tagger

The tag counts of the representative training split (`counts/`) train the Basic tagger. Measured on the convention-mapped UD-EWT development and test files and on the open test split (evaluation only; the treebank is not an input): see `docs/build_notes/basic-evaluation.md`. On tokens that the Basic classifier answers, the table trained on `counts/` scores 93.85% (91.48% on the ambiguous tokens) on the open test split, 91.76% (88.82%) on UD-EWT dev and 91.63% (88.58%) on UD-EWT test; the UD-trained table it replaces scored 89.88% (84.84%), 89.99% (86.05%) and 90.31% (86.52%). The part of that gain on the treebank that comes from the convention difference (the treebank tags the copula `be` AUX; we tag it VERB) is large, and the open test split shares annotator, guideline and genres with the training split, so the absolute numbers are optimistic.
