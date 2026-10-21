<!-- markdownlint-disable MD013 MD056 MD060 -->

# Annotation Quality

Measured on 2026-10-20. Numbers come from `scripts/evaluate.py` (`eval/ud_dev_scores.json`, `eval/pilot_scores.json`) and, for the history, from the archived score files under `eval/v1/` and `eval/v2/`.

## 1. Final pipeline

- **Primary annotator: gpt-6.1-sol** (UPOS, lemma, features), general-purpose sub-agents of the GitHub Copilot CLI, batches of up to 50 sentences in parallel. The final tag is always its tag.
- **Second annotator: gemma4:26b** (Ollama, UPOS only, temperature 0, seed 42). It is used only as a **flag**: a token where it disagrees gets consensus status `disputed`, otherwise `unanimous`. There is **no adjudicator**.
- Guideline: `GUIDELINE.md` version 3 (hard cases rebuilt from our own pilot disagreements with gpt-6.1-sol's help, `guideline-derivation.md`).

### Why no adjudicator and why gpt-6.1-sol is the primary

Measured earlier (outside this folder, `llmtag/results-sol.md`, an exploratory measurement with an earlier guideline on a different 500-sentence sample of UD English-EWT dev; shown here only as the reason for the decision):

| Annotator | UPOS | Collapsed |
| --- | --- | --- |
| gpt-6.1-sol | 96.41% | 97.25% |
| claude-sonnet-5.5 | 95.49% | 96.31% |
| gpt-5.6-luna | 92.54% | 93.51% |
| gemma4:26b | 90.30% | 92.12% |

No tie-break or majority policy beat gpt-6.1-sol alone on that sample (sol alone 96.41 / 97.25; sol/sonnet with a gemma tie-break 96.04 / 96.83; luna+sonnet agree else sol 95.78 / 96.54; majority of sol, sonnet, luna 95.78 / 96.54).

Our own earlier measurements in this folder agree: **keep-primary beat every adjudication policy that was tested.** On the 300-sentence UD sample, with claude-sonnet-5.5 as primary and the first-draft guidelines:

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

## 2. Method of the final measurement

The final pipeline (sol primary, gemma flag, guideline v3, same few-shot examples) was run on a fixed sample of **300 sentences (3,597 tokens) from UD English-EWT dev** (pinned commit `4a4d77f599ea53cc405f85d0cec4b2f14f81d42b`; `en_ewt-ud-dev.conllu` SHA-256
`39239E0A60DB3AE68F4B7036189F11B6692741D10FF8240DD91F74F2760D90F8`, verified by the script before use). The tokens given to the annotators are the UD word tokens so that comparison is token by token; UD tags were never shown to any annotator.
The sample uses seed 3003 over dev sentences of at most 40 tokens, excluding the 500 sentences of an earlier exploratory test; ids are in `eval/ud_dev_sample_ids.txt` (ids only). **UD-EWT dev was used solely to measure annotation accuracy; it was never
input to annotation (no tags, text or documentation of any UD treebank was given to any annotator, judge or prompt) and never used for training or tuning. UD test was never used.** No UD text is stored in this folder; the evaluation inputs and outputs were kept outside it.

**What "reference" means here.** UD-EWT follows its own conventions, which our guideline deliberately does not copy. Every convention difference counts as an error below, so the figures are a *lower bound* on the accuracy against our own guideline, and EWT (weblogs, e-mail, reviews,
answers, newsgroups) is a different genre mix from the pilot sources.

## 3. Results on the UD English-EWT dev sample (final pipeline)

| System | UPOS accuracy | Collapsed (12-tag) accuracy |
| --- | --- | --- |
| **Final tag (gpt-6.1-sol, primary)** | **93.08%** | **94.16%** |
| Second annotator alone (gemma4:26b) | 90.21% | 91.66% |

Lemma accuracy of the primary annotator (case-insensitive match with the reference lemma): 98.11%.

### By consensus status

| Status | Tokens | Share | UPOS accuracy | Collapsed accuracy |
| --- | --- | --- | --- | --- |
| unanimous (gemma agrees with sol) | 3,378 | 93.91% | 94.58% | 95.35% |
| disputed (gemma differs) | 219 | **6.09%** | 69.86% | 75.80% |

On the 219 disputed tokens the final (sol) tag matched the reference 153 times (69.9%), gemma's tag 50 times (22.8%), neither 16 times (7.3%): keeping sol's tag was right three times as often as gemma's.
Using only the unanimous tokens (94% of the corpus) gives 94.58% UPOS accuracy against the reference.

### Why this is below the earlier 96.4% measurement of sol

The earlier measurement used a guideline whose conventions were closer to the reference. Version 3 deliberately takes traditional-grammar choices that differ from it. The most frequent confusions (reference tag -> our tag) are
PRON->DET 50 (possessive words such as `my`, DET in our guideline), VERB->ADJ 22, PROPN->NOUN 15, PROPN->X 15 (URLs and e-mail addresses are X for us), SCONJ->ADP 14 (preposition before a gerund is ADP), ADP->ADV 12 (particles of phrasal verbs are ADV),
DET->PRON 10, ADJ->PROPN 9, NOUN->PROPN 9, VERB->AUX 8 (existential `be` is AUX). The four deliberate convention differences alone (PRON->DET, SCONJ->ADP, ADP->ADV, VERB->AUX: 84 tokens, tag pairs only, not audited token by token) account for about 2.3 points,
so against a reference that shared our conventions the final accuracy would be roughly 95 to 96%. If the owner prefers the possessive words as PRON, the guideline change is one line (and it would recover about 1.4 points on this reference).

## 4. History: guideline versions on the same sample

| Run | Primary | Guideline | Primary UPOS / collapsed | Agreement with gemma (UPOS) |
| --- | --- | --- | --- | --- |
| first draft | claude-sonnet-5.5 | v1 (partly UD-informed, superseded) | 95.47% / 96.36% | 92.97% |
| intermediate | claude-sonnet-5.5 | v2 (round 1 of our own pilot data) | 93.02% / 94.25% | 94.05% |
| **final** | **gpt-6.1-sol** | **v3 (round 2, with sol)** | **93.08% / 94.16%** | **93.91%** |

The drop from v1 to v2 is the intended loss of closeness to the reference's conventions (independence was the goal); v2 to v3 changes the UD-sample accuracy little but the annotators agree more on our own sentences.

## 5. Inter-annotator agreement on the pilot (final pipeline)

| Data | Tokens | UPOS agreement | UPOS kappa | Collapsed agreement | Collapsed kappa |
| --- | --- | --- | --- | --- | --- |
| **Pilot (1,000 sentences)** | 17,374 | **95.46%** | 0.9495 | 95.84% | 0.9528 |
| UD sample (300 sentences) | 3,597 | 93.91% | 0.9328 | 94.88% | 0.9420 |

Pilot agreement by group (UPOS): conversational 96.75%, news 96.16%, technical 95.17%, literary 94.44%.
Pilot consensus status: **unanimous 16,586 tokens (95.46%), disputed 788 tokens (4.54%)**. For comparison, the first draft (v1, sonnet versus gemma, different sentences) agreed on 94.89% of tokens, and sol versus gemma with the v2 guideline on this same pilot agreed on 95.36%;
sol versus sonnet agreed on 99.36% (112 disputed tokens), which shows that the two strong annotators read the guideline alike and most disputes come from the weaker second annotator or from genuine ambiguity.

Most frequent disputed pairs in the pilot (unordered): DET/PRON 77, ADJ/NOUN 66, ADP/SCONJ 62, NOUN/PROPN 56, AUX/VERB 56, ADJ/ADV 53, ADJ/VERB 50, ADJ/PROPN 36, ADP/ADV 34, ADV/SCONJ 31, ADP/PART 30, NOUN/VERB 23, ADJ/DET 18.

## 6. Indicative accuracy of the pilot

If the pilot behaves like the UD sample, the unanimous tokens (95.5%) are about 94.6% correct against a reference with other conventions and the disputed tokens (4.5%) about 70% correct, about 93.5% overall UPOS accuracy against that reference
and an estimated 95 to 96% against our own conventions. This is an extrapolation from a different genre mix and convention set, not a measurement on the pilot; no human has checked the pilot tags. Lemmas and features were produced by the primary annotator alone and
have no second opinion.

## 7. Caveats

- Annotators are LLMs; errors correlate (all models may have seen public treebanks in pretraining), and agreement cannot detect an error all of them share.
- The second annotator checks UPOS only. A lemma that depends on the tag (a noun/verb pair such as `saw`) is not recomputed.
- The sub-agent stages cannot set a temperature or seed, so the primary stage is not bit-reproducible; raw outputs are kept in `annotations/` and the ingest scripts are deterministic.
- The measurement was made on the same 300 sentences that informed the choice between policies (keep-primary vs judges); the differences between policies are a few tokens and should not be over-read.
