# Lexicon Evaluation

This note records how the Moby-derived dictionary (the "lexical section" of the v5 table) was built, evaluated against
Universal Dependencies English-EWT and gated, and what it costs in size, load time, memory and speed. The size, time and
speed sections describe the table as it was measured before the lexicon was completed.

## Source and Filters

The dictionary comes from Moby Part-of-Speech II (public domain, Grady Ward, <https://www.gutenberg.org/ebooks/3203>).
The input `mobypos.txt` (3,004,202 bytes) has SHA-256
`AB516C4918E017D7771DA70766EFB95C6C9B81DCECC97395FE0B61EE5373582D`. The importer (`import-moby`) is deterministic:
Latin-1 read, entries split on LF, each entry `word\codes`; a word on several lines has its codes joined.

| Step | Count |
| --- | ---: |
| Entries read | 233,356 |
| Single lowercase alphabetic words | 143,992 |
| Dropped | 0 |
| Words written to `lexicon.tsv` | 143,992 |
| of which carry the determiner sense | 103 |

Earlier versions of this table dropped 280 of the 143,992 words: 103 because of a determiner code (D, I, o) and 177
because their only parts of speech were Conjunction or Interjection, which failed the accuracy gate. That was wrong:
the gate decides which sense a model may choose, never whether a word exists. Every word is now kept and all of them
are in the embedded table. Code mapping: N, p, h are Noun; V, t, i are Verb (V is the ordinary verb flag); A
Adjective; v Adverb; C Conjunction; P Preposition; ! Interjection; r Pronoun; D, I, o Determiner. The parts of speech
of a word are written in the order of its first code in the Moby entry.

The table embeds 254,511 words. Of the 1,227 words that a trained lemma or a trained form also spells, 238 had lost a
part of speech under the earlier merge; the merge now keeps the trained senses first and appends the Moby parts of
speech that the trained entry lacks (they are never ranked by a trained classifier).

## Corrections and Rules

- Gerund rule: an `-ing` word of five or more letters that has no verb code gains Verb when its stem (plain, plus `e`,
  or undoubled consonant) is a Moby verb. Without it, words such as being, doing and making were noun-only and the
  Noun class failed at 90.8 percent.
- `corrections.tsv`: i is Pronoun; days is Noun; needs is Noun and Verb; says and comes are Verb. The last four were
  chosen from the most frequent evaluation errors on the pooled UD data, so they carry a small risk of overfitting.
  `the`, `a`, `an`, `his` and `my` are Determiner only: Moby also lists adverb, noun, verb, preposition, conjunction
  and pronoun readings for them, and UD labels `the`, `a` and `an` determiner in over 99 percent of their uses.
- Held-back classes (`classes.tsv`): Conjunction (76.37 percent) and Interjection (86.36 percent) fail the 95 percent
  gate. They are never the target of a trained model; every word of these classes is kept in the table, as an answer
  for a word that has only that part of speech and as a candidate for a word that has others. Determiner is also
  candidate-only.
- `PartOfSpeech.Determiner` was added because `the`, `a` and `an` are the most frequent words of English and no other
  value fits them. No Modal value was added: `will` is a verb in the enumeration (UD AUX is scored as Verb) and the
  data does not support a separate class.

## Accuracy After Completing the Lexicon

The Basic model measured on the committed holdout and real-text sets before and after (the encoder models are
unchanged because their trained lemmas and weights are unchanged and the added words are single-sense or untrained):

| Model | Holdout before | Holdout after | Real text before | Real text after |
| --- | ---: | ---: | ---: | ---: |
| MiniLM | 96.87 % | 96.87 % | 92.60 % | 92.60 % |
| BERT base cased | 99.05 % | 99.05 % | 97.39 % | 97.39 % |
| Basic | 88.65 % | 89.02 % | 84.76 % | 85.49 % |

The earlier Basic figures are those of the committed model table before this change. No threshold or hyperparameter
was tuned on the holdout.

A multi-sense word that has no trained weights (for example `so`) is answered by an encoder model with its first
Moby-order sense and a confidence of one over the number of senses; this fallback was approved because the sense order
follows the Moby code order, which lists the most frequent part of speech first.

## Evaluation Method

Gold data is UD English-EWT train, dev and test pooled. Mapping: NOUN Noun; VERB and AUX Verb (decided: AUX is a verb
in the enumeration); ADJ; ADV; PRON; ADP Preposition; SCONJ is accepted as Preposition or Conjunction; CCONJ
Conjunction; INTJ. PROPN, PUNCT, NUM, SYM, X, DET and PART are not scored. Multiword-token parts (such as `ca` and
`n't`) are collapsed into one unscored token. The case policy skips capitalized mid-sentence tokens. A rule is measured
on a copy of the table with all other rules disabled. The gate is 95 percent per class and rule; a figure over fewer
than 100 tokens is marked thin but does not fail.

### Per-Class Precision (direct answers)

| Class | Correct | Total | Precision | Gate |
| --- | ---: | ---: | ---: | --- |
| Noun | 13241 | 13565 | 97.61 % | pass |
| Pronoun | 7501 | 7509 | 99.89 % | pass |
| Verb | 15777 | 16133 | 97.79 % | pass |
| Adjective | 3403 | 3499 | 97.26 % | pass |
| Adverb | 3794 | 3911 | 97.01 % | pass |
| Preposition | 7153 | 7163 | 99.86 % | pass |

### Morphology Rules

| Rule | Correct | Total | Precision | State |
| --- | ---: | ---: | ---: | --- |
| Plural | 3593 | 3630 | 98.98 % | enabled |
| PastTense | 1551 | 1638 | 94.69 % | disabled (fails the gate) |
| Adverb | 57 | 57 | 100.00 % | enabled (thin evidence) |

### Candidate-Set Recall (multi-part-of-speech words)

The gold part of speech is in the candidate set 97.32 percent of the time (73,942 of 75,976); for two-candidate words it
is 96.79 percent.

### Coverage and Encoder Skip

Of 16,622 sentences, 8,610 (51.8 percent) need no encoder call. Token census: 53,813 dictionary one-POS,
80,382 dictionary multi-POS, 5,374 morphology, 13,369 trained (context), 40,829 unknown, 9,203 capitalized unknown,
8,838 skipped by the case policy; 2,082 answered tokens have an unscored gold tag.

## Size

| Item | Bytes |
| --- | ---: |
| Table before the lexicon (v5, empty lexical section) | 1,307,654 |
| Table with the lexicon | 1,676,380 |
| Lexical section (difference) | about 368,700 |
| NuGet package (`dotnet pack`, Release) | 5,104,920 |

The 1.5 MB target is exceeded (1.68 MB), as the plan anticipated. The trained part (744 lemmas, 2,304 forms, weights
and mean) is bit-identical to the pre-lexicon table.

## Load Time and Memory

Cold `WordSenseTable.LoadEmbedded()` (historical; this API was replaced by the layered model packages) in a fresh
process (net10.0, Release), five runs: 86.7, 77.4, 71.9, 80.2 and 73.8 ms. Allocated 12,023,832 bytes per load;
about 3.18 MB retained after a full GC.

## Speed

300 UD English-EWT dev sentences with the real model, 154 encoder calls in both modes and 146 sentences with zero
encoder calls in both modes. The lexicon therefore does not change encoder work: the encoder is needed only for
trained lemmas, and the lexicon answers words that were otherwise unknown. Throughput measured 307 and 68
sentences/s with `UseLexicon` false and 498 and 34 sentences/s with it true across two passes; the spread between
passes is larger than any difference between modes, so the timing is noise from machine load and no speed effect is
claimed.

## Reproduction (historical)

These commands are the ones used when the page was measured; `build` and `lexicon-eval` no longer
exist in `WordSense.Builder` (see `package-split.md` and the `words` and `weights` commands for the current flow).

```text
dotnet run --project tools/WordSense.Builder -c Release -- import-moby --input mobypos.txt --output corpus/lexicon/lexicon.tsv
dotnet run --project tools/WordSense.Builder -c Release -- build --model model --model-id all-MiniLM-L6-v2-int8 \
  --corpus corpus --output src/DemaConsulting.WordSense/Resources/wordsense-lexicon.bin \
  --weights src/DemaConsulting.WordSense.MiniLM/Resources/weights.all-MiniLM-L6-v2-int8.afdb6f1a0e45b715d0bb9b11772f032c399babd23bfc31fed1c170afc848bdb1.bin
dotnet run --project tools/WordSense.Builder -c Release -- lexicon-eval \
  --table src/DemaConsulting.WordSense/Resources/wordsense-lexicon.bin --ud tools/real-text/cache \
  --output report.md --enforce
```

Since the package split the dictionary lives in the lexicon table (`wordsense-lexicon.bin`, in the core package) and
the trained weights in a separate weights table per model, so `lexicon-eval` takes the lexicon table. The page
describes the table as it was measured, before the split; the dictionary content is unchanged.
