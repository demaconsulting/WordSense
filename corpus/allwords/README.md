# The all-words gold set

An independent evaluation set for the **usage prior** of the senses of the lemmas that no model scores: 804 tokens of
untrained polysemous lemmas (569 lemmas) from running text. Two independent labelers chose the sense of each token
without seeing the frequency of the senses.

It is evaluation data only: no training step reads it. The leakage check (`WordSense.Builder check-leakage`) treats each
file as a view of the split of the open corpus it is drawn from.

SemCor cannot measure that prior, because the WordNet 3.0 tag counts the prior is made of were counted on SemCor. The
tokens of this set are not in SemCor and no count was made on them.

## Files

<!-- markdownlint-disable MD013 -->

| File | Content |
| ---- | ------- |
| `dev.tsv` | 407 tokens of 333 lemmas from the sentences of the **development** split of `corpus/open/representative`; the half on which the smoothing constant of the prior is chosen |
| `test.tsv` | 397 tokens of 323 lemmas from the sentences of the **test** split of `corpus/open/representative`; reported only, never tuned on |

<!-- markdownlint-enable MD013 -->

The two halves come from different documents (the splits of the open corpus are made by document), so a choice made on
the development half cannot leak into the test half.

A line has nine tab-separated columns after the header comment lines (`#`):

<!-- markdownlint-disable MD013 -->

| Column | Content |
| ------ | ------- |
| `id` | the id of the token in the draw: `d-` or `t-` and a number (the numbers of dropped tokens are missing) |
| `sent_id` | the `sent_id` of the sentence in the open corpus (`corpus/open/sources.tsv` has its source) |
| `target` | the zero-based index of the token in the sentence |
| `form` | the token as written |
| `lemma` | the lemma of the token, from the annotation of the open corpus |
| `pos` | the part of speech: `Noun`, `Verb`, `Adjective` or `Adverb` |
| `sense` | the gold sense id (`lemma#label`), a sense of the lemma and part of speech in the catalog |
| `agreement` | `high` when both labelers were highly confident, otherwise `low` |
| `sentence` | the words of the sentence separated by single spaces, as a model receives them (the contractions the open corpus splits are joined again) |

<!-- markdownlint-enable MD013 -->

## Selection

The tokens are drawn by `WordSense.Builder allwords-sample` (seed 1) from the development and the test split
separately. A token is a candidate when:

- the open corpus tags it as a noun, verb, adjective or adverb, and the two annotators of the corpus did not dispute
  the tag (`consensus` is not `disputed`);
- the lemma is **not trained**: it has no sentence in `corpus/anchors`, so no encoder has weights for it (this is the
  lemma set of the SemCor all-words evaluation);
- the part of speech of the token has **two or more senses** in the catalog, and the lexicon lists the lemma and part
  of speech for the written form (so a model could answer it);
- the word is made of letters only.

The candidates are put in a pseudo-random order (a hash of the seed, the sentence id and the position) and taken in that
order. The draw is frequency-weighted (a frequent word has more candidates) and reproducible. It takes **at most three
tokens of a lemma** and one token of a sentence, 600 for each split (1,200 in all).

## Labeling

Two independent labelers labeled every one of the 1,200 tokens blind: **gpt-6.1-sol** and **claude-sonnet-5.5**. Each
used the instructions of `tools/quota-fill/prompts/sentence-labeler.md` and batches of 100 tokens in the format of the
quota filling (`groups` of a lemma and part of speech with `senses` and `items`).

A labeler saw the sentence with the target word in braces and **every sense of the lemma and part of speech** with its
id, part of speech and gloss, in the order of the sense ids. It never saw a tag count, the order of the catalog or the
answers of the other labeler. It answered a sense with a confidence (`high` or `low`) or `unsure`.

The batches and the answers are not kept in the repository (the glosses are Open English WordNet data under CC BY 4.0).
The sentences, the tokens and the chosen senses are the data of this folder.

A token is **kept when both labelers chose the same sense**, whatever their confidence. An `unsure` answer of either
labeler, a missing answer and a disagreement drop it (`WordSense.Builder allwords-merge`).

<!-- markdownlint-disable MD013 -->

| Split | Sampled | Both answered a sense | Same sense | Agreement (of both answered) | Same sense, both high | First labeler unsure | Second labeler unsure | Kept (share of sampled) | Lemmas |
| ----- | ------: | --------------------: | ---------: | ---------------------------: | --------------------: | -------------------: | --------------------: | ----------------------: | -----: |
| dev | 600 | 444 | 407 | 91.67% | 344 | 90 | 114 | 67.83% | 333 |
| test | 600 | 433 | 397 | 91.69% | 311 | 86 | 129 | 66.17% | 323 |
| all | 1,200 | 877 | 804 | 91.68% | 655 | 176 | 243 | 67.00% | 569 |

<!-- markdownlint-enable MD013 -->

The kept tokens are 203 and 204 nouns, 89 and 88 verbs, 74 and 81 adjectives and 41 and 24 adverbs (development and
test). They come from Project Gutenberg books, Voice of America, USGS, National Park Service and National Weather
Service texts and Tatoeba sentences. 655 of the 804 tokens (81.5%) carry the agreement `high`.

## Limits

- **The gold keeps what two labelers agree on.** A token on which a labeler was unsure or the two disagreed is dropped
  (33% of the sampled tokens). The set holds the cases where the sentence settles the sense, so it is probably easier
  than all running text. Figures measured on it are figures on settled tokens, not on every token.
- **The labelers are language models.** They agree on 91.7% of the tokens they both answered. Nobody has checked the
  agreed senses by hand: a sense that both chose wrongly is in the gold. There is no human annotation.
- **The part of speech is the annotation of the open corpus** (95% accurate against the convention-mapped treebank, see
  `corpus/open/quality.md`). A labeler who saw a word of another class answered `unsure`.
- **The sample is small**: about 400 tokens in each half. A difference of a few points between two figures is within the
  noise (the standard error of an accuracy of 65% on 400 tokens is 2.4 points).

## Current results

Measured by `evaluate-allwords`. The figures are in the chapter "Accuracy and Model Choice" of the user guide, in "The
Usage Prior". The three models give the same figures, because they answer an untrained lemma with the same Basic
result.

## Provenance and license

The sentences come from `corpus/open/representative` (development and test splits) and are public domain in the United
States or CC0 (see `corpus/open/sources.tsv`). The selection, the labels and the agreement are project-authored and are
dedicated to the public domain under **CC0 1.0** by the project owner, like the annotations of the open corpus. The
glosses that the labelers read are Open English WordNet data (CC BY 4.0) and are not part of this folder.

## Use

<!-- markdownlint-disable MD013 -->

```bash
dotnet run --project tools/WordSense.Builder -- evaluate-allwords      # both halves, every model, the baselines and the sweep
dotnet run --project tools/WordSense.Builder -- allwords-sample --batches DIR   # draw again and write the batches
dotnet run --project tools/WordSense.Builder -- allwords-merge --batches DIR --first A --second B
dotnet run --project tools/WordSense.Builder -- check-leakage         # the two halves are views of the open corpus splits
```

<!-- markdownlint-enable MD013 -->
