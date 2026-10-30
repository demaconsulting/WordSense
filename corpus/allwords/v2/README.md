# The second all-words gold set (v2)

An independent, document-disjoint test of **part of speech, tense, lemma and sense**. It has 5,072 tokens from 1,442 new
public-domain documents, drawn by strata before any model or labeler saw a token, and labeled by two language models
with a third adjudicating every disagreement. **No token is dropped, and no human reviewed a label.**

It exists because the first all-words gold set (`../README.md`) is too small and too easy for the questions that follow:
it has 209 untrained test tokens, 47% of its test tokens now belong to trained lemmas, it drops 33% of its sampled
tokens for uncertainty or disagreement, and its tense check was split by sentence, not by document.

It is evaluation data only: no training step reads it. The leakage check (`WordSense.Builder check-leakage`) treats the
two splits as parts of their own: a sentence that any other part of the repository also holds is a leak, and so is a
document that another part uses or has rejected.

<!-- markdownlint-disable MD013 -->

## Files

| File | Content |
| ---- | ------- |
| `manifest.tsv` | The 5,072 sampled tokens, **frozen in its own commit before any labeling**: id, split, document, source, sentence id, target index, form, strata, sentence |
| `sources.tsv` | The 1,442 documents: family, title, URL, author, year, license basis (the quoted statement), terms URL, retrieval date, SHA-256 of the retrieved file and of the extracted text, sentences contributed to the pool |
| `dev.tsv` | 2,025 labeled tokens of the **development** split (586 documents) |
| `test.tsv` | 3,047 labeled tokens of the **test** split (856 documents); reported only, never tuned on |
| `labels.tsv` | The raw answer of the first labeler, the second labeler and the adjudicator for every token (`Pos\|lemma\|features\|sense\|confidence`) |

The two splits share no document: documents are divided by a hash, family by family, so each split holds about 40% and
60% of the pooled sentences of every family. The rows of `dev.tsv` and `test.tsv` match the tokens of the manifest one to
one (id, document, source, sentence, target, form, strata and words); `evaluate-gold2` and `compare-gold2` refuse a set
whose rows are missing, duplicated, unknown or different.

A line of `dev.tsv` and `test.tsv` has 16 tab-separated columns after the header comment lines (`#`):

| Column | Content |
| ------ | ------- |
| `id` | `d-` or `t-` and a number |
| `doc_id`, `source`, `terms_url` | the document, its source family (`voa`, `usgs`, `nps`, `noaa-nws`, `gutenberg`) and the terms page of the source |
| `sent_id`, `target`, `form` | the sentence, the zero-based index of the token in it, the token as written |
| `strata` | the sampling strata of the token (below), comma-separated; `C` for a control token |
| `pos` | the gold part of speech: a part of speech of the lexicon or `Other` (a token that is no word class of the lexicon) |
| `lemma`, `features` | the gold lemma and the features of the gold analysis (`-` when none) |
| `tense` | for a verb whose gold analysis has features, `past` (past tense or past participle) or `nonpast` (infinitive, present, imperative); `-` for any other token and for the 14 verbs whose gold analysis has no features (6 development, 8 test: `provided`, `interfaced`, `broadcast`, `come`, `dove`) |
| `sense` | the gold sense id; `-` when the analysis has fewer than two senses; `none` when no single sense is defensible |
| `status`, `agreement` | `agreed` when the labelers settled the token alone, else `adjudicated`; what the labelers agreed on: `full`, `analysis`, `pos` or `none` |
| `sentence` | the words of the sentence separated by single spaces, as a model receives them |

## Sources

Only the kinds of source that the open corpus admits (`../../open/README.md`, "Sources and license basis"), and only
documents that appear nowhere else in the repository: not in `corpus/open` (used, candidate list, rejected, downloaded),
not in the provenance of the anchors and held-out sentences, not in the first all-words set. A Project Gutenberg book is
one document whichever way a part writes it (`gutenberg-<number>` in the open corpus and here, `pg<number>` in the
provenance, or its address): the check compares one canonical key, and none of the 97 books of the set is in the anchors,
the held-out sentences, the open corpus or the first all-words set. No FAA, NASA or NOS text
(excluded by the owner for the open corpus), no Tatoeba, no CC BY or CC BY-SA text, no Universal Dependencies text,
no consumer sentence. Pages were retrieved on 2026-10-29.

| Family | Documents with tokens | Dev tokens | Test tokens | Where the documents come from |
| ------ | --------------------: | ---------: | ----------: | ----------------------------- |
| VOA Learning English (news) | 443 | 608 | 916 | The staff-written articles of the discovery listings that the open corpus did not use (1,251 candidates; 737 of the candidates rejected for an AP, AFP, Reuters or copyright marker) |
| Project Gutenberg (general prose) | 97 | 608 | 916 | 100 books drawn at random from the Gutenberg catalog: English, one named author who was born 1790 or later and died 1930 or earlier, no translator, the landing page states "Public domain in the USA", front matter with no year after 1930 (and at least one year when the author died after 1900), fiction, essays, history, science and travel |
| USGS (technical) | 359 | 243 | 376 | The pages of the discovery listings that the open corpus did not use (763 candidates: featured stories, news releases, science snippets and FAQs); text only, pages with a copyright marker rejected |
| National Park Service (technical) | 349 | 291 | 358 | Nature and history pages of parks that the open corpus did not crawl (1,127 candidate pages; the documents with tokens come from 57 parks), same rules |
| National Weather Service (technical, maintenance and procedures) | 194 | 275 | 481 | The current NWS directives (PDF) of the series 010 operations, 030 maintenance, logistics and facilities, 050 safety and 080 science and technology (295 candidates); running headers and page numbers removed, a directive with a copyright marker rejected |

The terms pages were read again on 2026-10-29 and still carry the statements that `sources.tsv` quotes:

| Terms page | SHA-256 (first 16 digits) | Statement found |
| ---------- | ------------------------- | --------------- |
| <https://www.voanews.com/p/5338.html> | `77fd4745f47e2fa7` | "All text, audio and video material produced exclusively by the Voice of America is in the public domain" |
| <https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits> | `3edb6293b9b9d401` | "USGS-authored or produced data and information are considered to be in the U.S. Public Domain" |
| <https://www.nps.gov/aboutus/disclaimer.htm> | `c74222e99ad0c87f` | "is generally considered in the public domain" |
| <https://www.weather.gov/disclaimer> | `c08c1597ebd52b31` | "are in the public domain, unless specifically noted otherwise" |
| <https://www.gutenberg.org/policy/license.html> | `8a5137aecb0911dc` | "If you strip the Project Gutenberg license and all references to Project Gutenberg from the text, you are left with a text unrestricted by U.S. intellectual property law" |
| <https://www.law.cornell.edu/uscode/text/17/105> | `e0f425493dd80314` | "Copyright protection under this title is not available for any work of the United States Government" |

The pool of sentences (about 114,000 sentences of 2,548 documents) is not committed: the pages change, and `sources.tsv`
records the hash of every retrieved file. It was built with the sentence filter of the open corpus (4 to 40 tokens, a
capital start and a final stop, balanced quotes, no URL, at most three numbers), without sentences that carry a doubled
quotation mark or talk about words ("the word", "the phrase", "grammar": the VOA lessons), without a duplicate, and
without the 498 sentences that another part of the repository holds. The tokens are the ones of the open corpus
(punctuation is a separate word), with the contractions joined again as an application gives them.

## Selection

The draw (`WordSense.Builder gold2-sample`, seed 1) happens **before any model or labeler is consulted**: the strata come
from the lexicon and the anchors alone.

- **Candidates.** A token made of letters that the lexicon knows. A capitalized word inside a sentence (a name) and an
  acronym are not drawn.
- **Strata** (a token can bear several):
  `U` an untrained polysemous lemma (no sentence in `corpus/anchors`; two or more senses of the part of speech);
  `TR` a trained polysemous lemma;
  `T` a same-spelling verb, whose spelling is both a past and a non-past verb (`put`, `read`, `found`, `come`);
  `NV` a form with a noun and a verb analysis;
  `H` a heteronym spelling of the word table;
  `L` an inflected form of two or more open-class lemmas (`saw`, `left`, `leaves`);
  `C` a control token with none of these (function words and unambiguous words).
- **Quotas** are counts of the tokens that bear a flag, filled rarest first (`T`, `L`, `H`, `NV`, `U`, `TR`, `C`), by the
  weights technical 40%, news 30% and general prose 30%, over a fixed pseudo-random order of the candidates (a hash of
  the seed, the sentence and the position). At most two tokens per sentence, never adjacent; at most 8 tokens per
  document (18 for a Gutenberg book); at most 40 tokens of one form for `T`, 12 for `L` and `H`, 6 for `NV`, 3 for `U` and
  `TR`, 4 for `C`.

| Tokens bearing the flag | `T` | `L` | `H` | `NV` | `U` | `TR` | `C` | All |
| ----------------------- | --: | --: | --: | ---: | --: | ---: | ---: | --: |
| dev | 320 | 298 | 133 | 669 | 800 | 489 | 733 | 2,025 |
| test | 480 | 447 | 200 | 999 | 1,200 | 729 | 1,100 | 3,047 |

The sentences are public-domain text and the selection is ours. A lemma is **trained** when it has weights in the shipped
models: sentences in `corpus/anchors` for two or more of its senses (the rule of `WeightsTrainer`; 1,404 lemmas). A lemma
with sentences for one sense (`always`, `therefore`) has no classifier and is untrained. The draw flagged `U` and `TR` with
a looser rule (any sentence of the lemma) and the manifest is frozen, so `evaluate-gold2` computes the trained and
untrained results at evaluation time, by the gold lemma and from the weights of the shipped models; the results follow the
later stages of lemma expansion. It also prints the flags of the draw against the flags computed now. No token of the draw
changes its `U` or `TR` flag. Of the tokens whose gold lemma has sentences for one sense only (7 development, 11 test, all
of a monosemous analysis), none is scored on the sense, so the sense strata have the same tokens under either rule.

## Size and power

The sizes follow from a power calculation, made before the labeling (`WordSense.Builder gold2-power`). A paired
comparison of two models on the same tokens is a McNemar test; with `d` the share of tokens on which the two models
differ and `δ` the difference of their accuracies, it needs `n = (z(1-α/2)·√d + z(power)·√(d-δ²))² / δ²` tokens
(two-sided α = 0.05, power 80%). The discordance `d` comes from the spikes of the planned part-of-speech head and of
the nearest-neighbor senses: the POS head differs from Basic on 755 of 21,482 open-corpus test tokens (3.5%), 4.9% of
the treebank test tokens and 5.9% of the ambiguous words alone; on the tense of same-spelling verbs on 14.6% of 994
treebank tokens, 13.6% of 198 `read` tokens and 16.4% of 110 open-corpus tokens; the intervals of the sense
comparison on the first all-words set give 19% to 26%. No spike measured the lemma: its 12% is assumed.

| Measure | Minimum detectable improvement | Discordance | Test tokens needed | Test tokens scored | Improvement the scored tokens detect |
| ------- | -----------------------------: | ----------: | -----------------: | -----------------: | -----------------------------------: |
| Part of speech, all tokens | 1.0 point | 3.5% | 2,745 | 3,041 | 0.95 point (1.08 at 4.5%) |
| Sense, untrained lemmas | 4.0 points | 24% | 1,175 | 1,058 | 4.21 points |
| Sense, trained lemmas | 5.0 points | 20% | 626 | 680 | 4.80 points |
| Tense, same-spelling verbs | 5.0 points | 14% | 438 | 347 | 5.61 points |
| Lemma, lemma-ambiguous forms | 5.0 points | 12% | 375 | 447 | 4.58 points |

Two measures fall a little short of the plan: the tense (only 72% of the `T` tokens are verbs in the gold labels, so
347 of the planned 480 are scored; a verb whose gold analysis has no features has no tense and is not scored, which is
3 of the 236 verbs of the development `T` stratum and none of the test one) and the sense of untrained lemmas (a share of the `U` tokens is monosemous in the
gold labels). The planned part-of-speech head is expected to gain several points on the tense (spike: 86% to 96%), far
above what 347 tokens can detect. The development split is 40% of the set and detects about 1.16 points of part of
speech, 5.2 of the sense of untrained lemmas and 6.8 of the tense: it is for choosing settings, not for confirming them.
The labeling was capped at 6,000 tokens; 5,072 were drawn (the strata overlap).

## Labeling

Three models labeled every token, without any human: **gpt-6.1-sol** (first labeler), **claude-sonnet-5.5** (second
labeler) and **gpt-5.6-luna** (adjudicator).

A labeler saw the sentence with the token in braces, the **analyses of the lexicon for the written form** (`a1`, `a2`, ...,
each with lemma, part of speech and features, so the tense of a verb is a choice of analysis) and, for every analysis of
two or more senses, the senses with their glosses **in the order of their ids**: never a frequency, never a tag count,
never the answer of the other labeler. It answered the analysis (or `other:Pos:lemma` when none fits), the sense (or
`unsure`) and a confidence. The batches hold 100 tokens (the glosses are Open English WordNet data under CC BY 4.0 and
are not kept in the repository).

A token is **settled by the labelers** when both chose the same analysis and the same sense (whatever their confidence).
**Every other token goes to the adjudicator**: a different analysis, a different sense, an `unsure` of either labeler or
an invalid answer. The adjudicator saw the same lists and both answers (as "labeler 1" and "labeler 2"), decided alone,
and could answer `none` when no single sense is defensible. That is kept as a category (`sense = none`) and left out of
the sense accuracy. The adjudicator was told that the infinitive marker `to` takes the preposition analysis (the lexicon
has no particle class); the first labelers had no such rule.

| Split | Tokens | Same part of speech | Same lemma | Same tense class (verbs) | Same analysis | Same sense (both chose one, same analysis) | Settled by the labelers | Adjudicated | No defensible sense |
| ----- | -----: | ------------------: | ---------: | -----------------------: | ------------: | -----------------------------------------: | ----------------------: | ----------: | ------------------: |
| dev | 2,025 | 98.02% | 99.21% | 99.35% | 97.09% | 88.06% | 84.69% | 15.31% | 17 |
| test | 3,047 | 97.57% | 99.05% | 99.85% | 96.82% | 86.88% | 84.97% | 15.03% | 20 |
| all | 5,072 | 97.75% | 99.11% | 99.64% | 96.92% | 87.34% | 84.86% | 15.14% | 37 |

<!-- markdownlint-enable MD013 -->

768 tokens went to the adjudicator. In 349 of them it chose the analysis and sense of the first labeler, in 325 those of
the second, and in 94 neither. Of the tokens on which the two labelers chose the same analysis, the first was unsure of
the sense of 241 and the second of 99 (all tokens: 244 and 105). Seven tokens have the part of
speech `Other` (a fragment such as `ki` or `Y`, and an infinitive `to` that the first labelers did not map); they are
left out of the part-of-speech accuracy.

## Baseline

Measured once by `evaluate-gold2` on the shipped models (Basic, MiniLM, BertBaseCased), on both splits, with nothing
tuned on the test split. Part of speech and lemma are those of the selected analysis (a token a model does not answer is
wrong); the tense is the past or non-past class of the selected analysis on the same-spelling verbs; the sense is the
first sense of the selected analysis (the gold analysis is not told to the model). The last column is a baseline that is
**told the gold lemma and part of speech**: the sense with the highest WordNet 3.0 tag count.

**Test split** (3,047 tokens):

| Measure and stratum | Tokens | Basic | MiniLM | BertBaseCased | Most frequent sense (told lemma and part of speech) |
| ------------------- | -----: | ----: | -----: | ------------: | --------------------------------------------------: |
| Part of speech, all | 3,041 | 94.34% | 94.34% | 94.25% | n/a |
| Part of speech, noun/verb words | 999 | 95.50% | 95.40% | 95.30% | n/a |
| Lemma, all | 3,047 | 97.28% | 97.44% | 97.47% | n/a |
| Lemma, lemma-ambiguous forms | 447 | 84.56% | 85.68% | 85.91% | n/a |
| Tense, same-spelling verbs | 347 | 90.78% | 89.34% | 89.05% | n/a |
| Sense, all | 1,738 | 47.87% | 52.59% | 54.03% | 50.81% |
| Sense, untrained lemmas | 1,058 | 51.13% | 51.04% | 51.04% | 53.12% |
| Sense, trained lemmas | 680 | 42.79% | 55.00% | 58.68% | 47.21% |
| Sense, heteronyms | 185 | 52.97% | 70.81% | 81.08% | 55.14% |
| Sense, noun/verb words | 887 | 43.40% | 52.31% | 54.45% | 46.34% |

**Development split** (2,025 tokens):

| Measure and stratum | Tokens | Basic | MiniLM | BertBaseCased | Most frequent sense (told lemma and part of speech) |
| ------------------- | -----: | ----: | -----: | ------------: | --------------------------------------------------: |
| Part of speech, all | 2,024 | 94.76% | 94.86% | 94.71% | n/a |
| Part of speech, noun/verb words | 669 | 95.52% | 95.81% | 95.37% | n/a |
| Lemma, all | 2,025 | 97.48% | 97.63% | 97.68% | n/a |
| Lemma, lemma-ambiguous forms | 298 | 86.58% | 87.58% | 87.92% | n/a |
| Tense, same-spelling verbs | 233 | 93.13% | 91.85% | 92.27% | n/a |
| Sense, all | 1,134 | 48.59% | 51.94% | 54.67% | 50.88% |
| Sense, untrained lemmas | 697 | 53.52% | 53.52% | 53.52% | 55.52% |
| Sense, trained lemmas | 437 | 40.73% | 49.43% | 56.52% | 43.48% |
| Sense, heteronyms | 115 | 53.91% | 66.96% | 74.78% | 56.52% |
| Sense, noun/verb words | 570 | 44.56% | 51.23% | 56.14% | 46.84% |

On untrained lemmas the three models answer with the same usage prior, so they agree (the encoders have no weights
for these lemmas); the prior is below the most frequent sense told the gold analysis because the models also choose
the analysis. The three models have the same part of speech and the same lemma accuracy within 0.2 points, and the
paired comparison on the test split finds no significant difference in part of speech (for BertBaseCased against
Basic: 11 tokens right only for BertBaseCased, 14 only for Basic) and none in tense. `evaluate-gold2` prints every
stratum, the source groups (technical, news, literary) and the paired comparisons with their intervals.

## Limits

- **The labels are model output.** No human reviewed them: the owner chose language-model adjudication. A sense or an
  analysis that all three models got wrong is in the gold, and the three models may share errors. The agreement of the
  two labelers (97% on the analysis, 87% on the sense) is not accuracy.
- **The set is enriched.** It over-represents ambiguous words, same-spelling verbs, heteronyms and untrained polysemous
  lemmas, and under-represents ordinary function words. Its accuracies are not the accuracy on running text (the open
  corpus test split is): compare models on the same stratum, never the set with another set.
- **Names and capitalized words inside a sentence are not drawn**, so the part of speech of a proper noun is not
  measured. Seven tokens are an exception: single capital letters inside a sentence (`C`, `D`, `E`, `Y`, `F`, `J`; ids
  `d-0715`, `d-0833`, `d-0925`, `t-1282`, `t-1440`, `t-1518`, `t-2657`), which the draw let through because it exempted
  every single letter, not only `I`. The manifest is frozen, so they stay in the set and are labeled and scored like the
  others (0.14% of the tokens).
- **The analyses are those of the lexicon.** A token the lexicon cannot analyze is `other:...` and has no sense; the
  part of speech `Other` is not scored.
- **Genre.** English prose of five families: news (the plain-English register of VOA Learning English), US government
  informational and procedural text (USGS, NPS, NWS directives) and 19th and early 20th century books. There is no
  conversation, no social text and no maintenance manual of an aircraft or a vehicle: the technical text is geology,
  parks and weather service procedures.
- **Two measures are a little under the plan** (tense and the sense of untrained lemmas; see "Size and power").
- **Documents are clusters.** Tokens of one document are not independent; the paired comparison resamples documents.

## Provenance and license

The sentences are public domain in the United States (works of US federal employees, 17 U.S.C. §105, and Project Gutenberg
books published 1930 or earlier) and keep that status. The selection, the strata, the labels and the agreement are
project-authored and are **dedicated to the public domain under CC0 1.0** by the project owner, like the annotations of
the open corpus. That dedication covers the labels, not the sentences. The labels are written by language models and are
not human-reviewed; the owner's position on the rights in model outputs is in the
[README](../../../README.md#credits-and-licenses). The glosses that the labelers read are Open English WordNet
data (CC BY 4.0) and are not part of this folder. `sources.tsv` credits every document by link, with the statement it
rests on and the date and hash of the retrieval; Project Gutenberg texts are public domain in the United States only.

## Use

<!-- markdownlint-disable MD013 -->

```bash
dotnet run --project tools/WordSense.Builder -- gold2-power                      # the power plan
dotnet run --project tools/WordSense.Builder -- evaluate-gold2 --outcomes DIR    # both splits, every model, the strata and the paired comparisons
dotnet run --project tools/WordSense.Builder -- compare-gold2 --first A.tsv --second B.tsv --split test    # two outcome files, for example of two builds
dotnet run --project tools/WordSense.Builder -- check-leakage                    # no shared sentence, no shared document
```

To draw and label again: `gold2-sample --pool FILE --docs FILE` writes the manifest, `gold2-batches --dir DIR` writes the
labeling batches (with `--stage adjudicate --first A --second B` the adjudication batches; with `--missing SUFFIX` only
the tokens without an answer, under a retry tag `-r1`, `-r2`, ... that leaves the tables of the earlier rounds as they
are), and `gold2-merge --dir DIR --first A --second B --adjudicator C` writes `dev.tsv`, `test.tsv` and `labels.tsv`.

<!-- markdownlint-enable MD013 -->
