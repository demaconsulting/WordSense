<!-- markdownlint-disable MD013 MD056 MD060 -->

# How the hard-case list of GUIDELINE.md (version 4) was derived

`GUIDELINE.md` Section 4 was built **only from our own pilot data**, in two rounds; version 4 then changed the `be` convention after a check against the WordSense lexicon (see "Version 4" below). No annotation of any outside treebank was used to choose the topics or the rules.
Earlier versions are superseded and kept in `archive/` for the record (see "Superseded drafts" below). The rules themselves are decisions by the guideline authors (LLMs, which
may know treebank conventions from pretraining; that cannot be excluded). Where two analyses were defensible, a rule takes the traditional-grammar category with a simple operational test.

## Round 1 (first pilot draw, version 1 -> version 2)

Disagreements between the two first-draft annotators (claude-sonnet-5.5 and gemma4:26b) on the first pilot draw, tagged with version 1: 732 tokens of 14,334 (5.1%) in 866 sentences of the retained sources
(Tatoeba, VOA, NWS, NOS, Gutenberg; FAA and NASA sentences of that draw were dropped for license reasons). The largest groups: ADP/SCONJ 110, AUX/VERB 77, DET/PRON 68, ADV/SCONJ 67, ADJ/ADV 45, ADJ/NOUN 43,
NOUN/PROPN 38, ADP/ADV 33, ADJ/PROPN 31, ADJ/VERB 25, ADP/PART 21, PRON/SCONJ 16, NOUN/VERB 15. This produced version 2 and the 20 few-shot examples (`prompts/fewshot.txt`), which are
sentences of that first draw, annotated by hand, and excluded from the current pilot's selection pool (`prompts/fewshot_ids.txt`).

## Round 2 (second pilot draw, version 2 -> version 3, with gpt-6.1-sol)

After the owner's decision to make gpt-6.1-sol the primary annotator, the second pilot draw (1,000 sentences, 17,376 tokens) was tagged with version 2 by gpt-6.1-sol and compared with
gemma4:26b and, for a cross-check, with claude-sonnet-5.5 (`scripts/disagreement_report.py`).

| Comparison | Disputed tokens | Share |
| --- | --- | --- |
| gpt-6.1-sol vs gemma4:26b | 807 | 4.64% |
| gpt-6.1-sol vs claude-sonnet-5.5 | 112 | 0.64% |

Pair counts of the sol/gemma disagreements (`archive/round2_pair_counts_sol_vs_gemma.tsv`) and of the sol/sonnet disagreements (`archive/round2_pair_counts_sol_vs_sonnet.tsv`) drove the rewrite.
A gpt-6.1-sol sub-agent was given the version 2 guideline and the token-level disagreement rows and asked to propose rules for every group of about 10 or more disagreements and for every group on which the two strong annotators differed.
Its proposal is archived unchanged (`archive/sol_rule_proposal.md`); the project then edited it into Section 4 (some proposals were adopted as written, others were reworded or rejected: for example, we did not adopt
`Internet` as NOUN, kept all forms of `be` as AUX, and kept the infinitive `to` rule). Counts in the Section 4 headings are the sol/gemma disagreements of this round.

| Rank | Pair | Count (strong/strong) | Guideline section |
| --- | --- | --- | --- |
| 1 | ADJ / NOUN | 82 (13) | 4.1 |
| 2 | DET / PRON | 77 (1) | 4.2 |
| 3 | ADJ / ADV | 59 (15) | 4.3 |
| 4 | ADP / SCONJ | 58 (1) | 4.4 |
| 5 | NOUN / PROPN | 54 (8) | 4.5 |
| 6 | ADP / ADV | 51 (14) | 4.6 |
| 7 | ADJ / VERB | 50 (15) | 4.7 |
| 8 | AUX / VERB | 46 (1) | 4.8 |
| 9 | ADV / SCONJ | 36 (5) | 4.9 |
| 10 | any pair with X (foreign words, fragments) | 31 (8) | 4.17 |
| 11 | ADP / PART | 28 | 4.10 |
| 12 | ADJ / PROPN | 27 (6) | 4.11 |
| 13 | NOUN / VERB | 20 (4) | 4.12 |
| 14-16 | ADJ / DET 15, ADV / DET 8, CCONJ / DET 6 | | 4.13 |
| 17 | PRON / SCONJ | 15 (1) | 4.14 |
| 18 | NUM / PRON 14, NOUN / NUM 5 | | 4.15 |
| 19 | PUNCT / SYM | 15 | 4.16 |
| 20 | ADJ / PRON 14, ADV / PRON 14, NOUN / PRON 5 | | 4.2, 4.3 |
| 21 | DET / PROPN (speaker labels) | 10 | 4.17 |
| 22 | small groups (ADV/NOUN, ADP/CCONJ, PROPN/VERB, ADV/INTJ, PART/PUNCT, AUX/PART) | 2-7 each | 4.19, 4.20 |

## What changed between versions (all driven by the data above)

- Version 1 -> 2 (round 1): prepositions before gerunds are ADP, `when` is SCONJ when it opens an adverbial clause, all forms of `be` are AUX, particles of phrasal verbs are ADV, members of a people are NOUN.
- Version 2 -> 3 (round 2): added the test for modifier nouns versus adjectives with the nominalized-adjective case (4.1); `her`, `others`, `plenty` and floating quantifiers (4.2); resultative adjectives after verbs and the PRON-versus-ADV test for `more` (4.3);
  the "own complement" scope for `before/after/until` and exceptive `but` (4.4); religions, historical events and decorative capitals (4.5); stranded prepositions (4.6); event-versus-state wording for participles (4.7);
  causative `have` (4.8); purpose `so`, relative `when/where` (4.9); `to` with an intervening adverb (4.10); `the` before a comparative and `no` as ADV (4.13); relative `that` with and without a gap (4.14); counting `one` (4.15);
  speaker labels (4.17); idioms, participial prepositions and titles (4.19); `right?` and `NB` (4.20).

## Version 4 (convention check against the WordSense lexicon, owner decision)

The corpus follows WordSense's own lexicon conventions. The four deliberate conventions of version 3 were checked against the lexicon's analyses (`data/form`, `data/sense`, `data/gloss` of the repository, read-only; details in `GUIDELINE.md` Section 9):
possessive determiners (`my`, `his`, `their`, `whose`, `its` are determiner-only; `her` is pronoun and determiner) **kept**; phrasal-verb particles as ADV (the adverb analyses of `up`, `out`, `off`, `down` carry directional/completive senses) **kept**;
prepositions before gerunds as ADP (`of`, `with` are preposition-only; `about`, `by` have no conjunction analysis; conjunction analyses exist only for clause openers) **kept**;
`be` always AUX **flipped**: every form of `be` has an auxiliary analysis (the helper in progressive and passive) *and* a verb analysis with main-verb senses (copula, existence, location, identity), so `be` is now AUX only as a helper and VERB as a main verb (copula, existence, location).
The few-shot examples were updated accordingly (seven `be` tokens). No sentence of any treebank was consulted.

## Superseded drafts

- Version 1 of the guideline had a hard-case list that was partly chosen from error categories seen in an earlier exploratory test of annotators against a public treebank. It is **superseded**; no published annotation was produced with it (`archive/GUIDELINE_v1_superseded.md`).
- Version 2 (`archive/GUIDELINE_v2_superseded.md`) was rebuilt from round 1 only and was used for the intermediate measurements in `quality.md`; version 3 (`archive/GUIDELINE_v3_superseded.md`) was used for the pilot published earlier; the published annotations of the current corpus come from version 4.

The few-shot examples are sentences of our own first pilot draw (Tatoeba and VOA), annotated by hand under the guideline; they are not part of the published pilot.
