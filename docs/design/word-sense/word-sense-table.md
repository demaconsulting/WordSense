# WordSenseTable Unit Design

## Overview

`WordSenseTable` is the one vocabulary of the library. It holds the model identity and SHA-256, the embedding
dimension, the anchor mean used for centering, every lemma as a `LemmaEntry`, a dictionary from lower-case word form
to lemma, the enabled morphology rules, and the single resolver that maps a surface word to a lemma entry. It is
stored as one embedded resource, `wordsense-table.bin`.

The unit contains `WordSenseTable.cs`, `LemmaEntry.cs` (with `LemmaModelKind`), `SenseWeights.cs`, `LexicalIndex.cs`,
`LemmaMorphology.cs`, `LemmaResolution.cs`, `VectorMath.cs` and `WordSenseTableFormatException.cs`. They are facets of
one lookup structure whose only job is "surface form to lemma entry", so they form one unit.

## Data Model

The uniform model is: surface form, lemma, `LemmaEntry`. A `LemmaEntry` is a lemma with a list of senses
(`SenseDefinition`: part of speech, optional meaning, optional respelling) and optional `SenseWeights`.

- A **dictionary word** is an entry with one sense and no weights. Its sense has no meaning, so it has no pronunciation.
  A dictionary word with several parts of speech is an entry with one sense per part of speech and no weights.
- A **context lemma** (a lemma with a trained classifier, for example `record`) is an entry with at least two senses
  and weights.
- There is no other kind of entry. The entry decides what happens to a word: weights present means the encoder is
  needed to choose; weights absent means no choice can be made from context.

Entry invariants, checked by the `LemmaEntry` constructor and again by the reader:

1. At least one sense; a (part of speech, meaning) pair is unique.
2. With weights: at least two senses, and the weights fit the senses (binary: two senses and one weight row;
   one-versus-rest: one row per sense).
3. Without weights: no sense carries a meaning, so `IsHeteronym` is false.
4. Either all senses carry a meaning or none does; a heteronym has at least two distinct meanings; one meaning has
   one respelling; distinct meanings have distinct respellings.
5. A lemma appears once, and a form never spells another lemma (a single namespace).

`SenseWeights` holds the mathematics that used to be part of the lemma model: `Kind` (`LemmaModelKind.Binary`: one
weight row, two senses, margin `2|a|`; `OneVersusRest`: one row per sense, margin between the best and the
second-best score), `Weights`, `Bias`, `AbstainMargin` and `Dimension`. `Scores(feature, scores)` fills one score per
sense (`{-a, +a}` for a binary kind with activation `a`); `Score(feature)` returns the best sense and the margin.

`LemmaEntry.Candidates` is one `WordDetails` per sense, in the order of `Senses`, built when the entry is
constructed; the answer a sense stands for is `(lemma, part of speech, pronunciation if any)`. The instances are
shared per entry instance. Context entries live for the life of the table, so their candidates are reference-equal on
every access. A dictionary entry is created on demand for each lookup (no cache, because a cache keyed by untrusted
input would grow without bound), so two lookups give equal but not reference-equal candidates.

### Physical stores

Two private stores sit behind the one public surface:

- The context entries (a few hundred) are kept as objects in an ordinal-ordered list with a name index.
- The dictionary words (about 143,700) are compiled into a `LexicalIndex`: a sorted set of lower-case `a-z` words,
  each with a mask of its parts of speech (bit `i` is `PartOfSpeech` value `i`), kept as one front-coded byte array.
  Nothing is expanded into per-word objects. Records are in blocks of 32 words; a block starts with a shared prefix
  length of 0. A lookup binary searches the block head words in place and decodes at most 32 records without
  allocating.

The public constructor takes every entry in one list, with or without weights, and partitions them; callers and the
builder never see two tables.

### Binary layout

Little-endian: magic `WSNS`, u16 format version (5), u16 flags (the enabled `LemmaMorphologyRules`), model id,
model SHA-256, u32 dimension, f32 mean vector, u32 trained lemma count, and per trained lemma: name, u16 sense count,
the senses (u8 part of speech, meaning string, respelling string; an empty string means "none"), then the u8 kind,
weights, biases and the f32 abstention margin. After the lemmas come the u32 form count and the forms (string and u32
trained-lemma index). Then follows the dictionary section: u32 word count, u16 block size (32), u32 decoded length,
u32 compressed length and the raw-DEFLATE compression of the front-coded records (u8 shared prefix length, u8 suffix
length, the suffix, u8 part-of-speech mask). A trailing SHA-256 digest covers all preceding bytes, so it covers the
weights and the dictionary together. The writer emits lemmas, forms and words in ordinal order, so output is
deterministic for a given runtime; the compressed bytes of the dictionary section can differ between runtimes, so
consumers that need identity compare the decoded content. The reader requires lemmas in ordinal order and accepts
forms in any order. Strings are a u16 byte length followed by UTF-8.

Format version 5 is the only version and any other version is rejected. A sense label is a build-time join key and is
not stored. The dictionary section is about 0.37 MB compressed (about 0.9 MB decoded); the table as a whole is about
1.7 MB, of which the trained weights are 1.24 MB.

## Resolution

`TryResolve(word, useDictionary, out LemmaResolution)` is the only way the classifier turns a surface word into an
entry. The word is lower-cased and a typographic apostrophe (U+2019) is straightened. The first rule that succeeds
wins:

1. **Form map**: an explicit inflection of a context lemma (for example `recorded` gives `record`).
2. **Exact lemma**: a context lemma, then (when `useDictionary`) a dictionary word.
3. **Possessive** `'s`: the stem is resolved by rules 1 and 2, accepted only for an entry that has weights.
4. **Gated morphology**, only when `useDictionary` is true and rules 1 to 3 failed, that is, only for a word the table
   does not contain and that has more than three letters (shorter words are never derived):
   `LemmaMorphology` derives candidate base spellings from the suffix, looks each base up as in rule 2 and requires
   the base to be a dictionary entry (no weights) that satisfies the rule. The base's `SenseIndex` is authoritative
   for which sense applies, and each rule shapes the entry differently: a plural returns a one-sense entry of the
   base; a past tense returns the full entry of the base, narrowed through `LemmaResolution.SenseIndex` to its verb
   sense; an adverb returns a synthesized one-sense Adverb entry for the base lemma. The plural rule never applies to
   a word ending in `-ss`, `-us` or `-is`. A resolution with a sense count of 1 or a narrowed `SenseIndex` is answered
   directly by the general flow.

The enabled rules are stored in the table flags, so the data decides what the code may do. Each rule is enabled only
if it reached the precision gate (at least 95 %) in the evaluation against a gold part-of-speech corpus:

| Rule | Suffix | Base requirement | Derived sense | Shipped |
| --- | --- | --- | --- | --- |
| Plural | `-s`, `-es`, `-ies` | exactly Noun, or exactly Verb | Noun, or Verb | enabled |
| PastTense | `-ed`, `-ied`, doubled consonant | has a Verb sense | Verb | disabled |
| Adverb | `-ly` | exactly Adjective | Adverb | enabled |

The past-tense rule is implemented and tested but is disabled in the shipped table: it measured 94.69 % precision,
which is below the gate (see `corpus/lexicon/morphology.tsv`), so the table flags enable only `Plural` and `Adverb`.
`-ing`, `-er`/`-est` and possessives of dictionary words do not exist because they did not reach the gate either. A
rule never overrides a word the table contains and never applies to an entry that has weights, so morphology cannot
create or alter a context answer. `LemmaResolution(Entry, SenseIndex)` is an internal record struct; `SenseIndex` is -1
when every sense applies. The lemma reported to callers is the lemma of the entry (the base for a form or a derived
word).

## Public Interface

- `WordSenseTable` properties: `ModelId`, `ModelSha256` (lower-case hexadecimal), `Dimension`, `Mean` (length
  `Dimension`), `Count` (every lemma), `ContextLemmas` (lemmas with weights, ordinal order), `Lemmas` (every lemma in
  ordinal order, dictionary lemmas decoded one at a time as the sequence is enumerated), `Forms` (form to context
  lemma) and `Morphology` (the enabled rules). Callers must treat these collections and the arrays of a
  `SenseWeights` as read-only; the constructors copy the arrays and the lemma list they are given but not the forms
  map.
- `LemmaEntry` properties: `Lemma`, `Senses`, `Candidates`, `IsHeteronym` and `Weights` (null for a dictionary lemma).
- `SenseWeights` properties: `Kind` (`LemmaModelKind.Binary` or `OneVersusRest`; any other value is rejected with
  `ArgumentOutOfRangeException`), `Weights`, `Bias`, `AbstainMargin` and `Dimension`.
- `WordSenseTableFormatException` is the only exception `Read` and `LoadEmbedded` report for bad content.

## Scoring Algorithm

`VectorMath.Dot(left, right)` sums the products over the length of `left`; `right` must be at least as long.
It uses `Vector<float>` SIMD lanes for full blocks and a scalar loop for the remainder, so any dimension gives
the same result as the plain scalar sum up to floating-point rounding. A binary entry computes one activation
`a = dot(weights, feature) + bias`; the scores are `{-a, +a}`, and the tie rule is that an activation of 0 or more
(`a >= 0`) selects sense index 1. A one-versus-rest entry scores each row and the first highest row wins a tie.

## Methods

- `LoadEmbedded()`, `Read(bytes)`: parse and validate; any truncation, bad magic, bad version,
  inconsistent count or digest mismatch raises `WordSenseTableFormatException`.
- `ToArray()`, `Write(stream)`: serialization as described above.
- `TryGetLemma(lemma, out entry)`: true with the entry for any lemma of the table, with or without weights, false for
  an unknown one; throws `ArgumentNullException` for a null lemma. It takes the lemma itself, not an inflected form.
- `SenseWeights.Score(feature)`: best sense index and margin; it throws `ArgumentException` when the feature length
  differs from the dimension. `SenseWeights.Scores(feature, scores)` throws `ArgumentException` when the feature
  length differs from the dimension or the scores length from the sense count.
- `LexicalIndex.TryFind(word, out mask)`: allocation-free lookup; anything but 1 to 255 letters `a-z` is simply not
  found.

## Error Handling

- The `WordSenseTable`, `LemmaEntry` and `SenseWeights` constructors throw `ArgumentNullException` for null
  arguments and `ArgumentException` for inconsistent content: a model SHA-256 that is not 64 hexadecimal
  characters, a mean that is not finite or does not match the dimension, lemmas that are duplicated, unordered or
  of another dimension, forms that name a lemma without weights, a form that spells another lemma, a dictionary lemma
  that is not 1 to 255 letters `a-z`, fewer than two senses with weights (exactly two for binary), weight or bias
  shapes that do not match, non-finite values, and an invalid sense set (an undefined part of speech, a malformed
  meaning or respelling, meanings given to only some senses, meanings on a lemma without weights, a heteronym with
  fewer than two distinct meanings, a repeated part of speech and meaning, inconsistent respellings of one meaning,
  or distinct meanings that share a respelling). `SenseWeights` also rejects a kind that is not defined
  (`ArgumentOutOfRangeException`), and a null element of the sense list or of the lemma list raises
  `ArgumentNullException`.
- `Write` throws `InvalidOperationException` when a string exceeds 65,535 UTF-8 bytes, and `Write` and `ToArray`
  throw `OverflowException` when a count does not fit its field (for example more than 65,535 senses in
  one lemma).
- `Read` verifies the trailing digest before trusting any length field, caps every count (dimension
  4096, trained lemmas 100,000, forms 2,000,000, senses per trained lemma 2 to 64, dictionary words 400,000 and
  4 MB decoded dictionary), inflates the dictionary only up to its declared decoded length (a guard against
  decompression bombs) and checks the structure: a defined part of speech for every sense, known model kinds,
  defined morphology rules, valid form-to-lemma indexes (in range and without a duplicated form), strings that are
  valid UTF-8, no trailing data, and in the dictionary strictly ascending words of letters `a-z`, a block head with
  prefix 0, a prefix no longer than the previous word, suffix lengths in range, a non-zero part-of-speech mask (the
  mask is one byte and all eight bits are defined parts of speech, so no bit can be undefined), an exact decoded
  length and no word that is also a trained lemma or a form. Every failure, including a
  constructor validation failure raised while reading, is reported as `WordSenseTableFormatException`.
- The read caps are enforced only by `Read`: the constructors and `Write` do not apply them, so a table
  built in memory can exceed a cap and be written, but `Read` will then reject it.
- A table with no dictionary words is valid (a small test table); the dictionary section then has a word count of 0.

## Interactions

Read by `WordSenseClassifier`, which calls `TryResolve`, `TryGetLemma`, `ContextLemmas` and the weights of an entry;
written by `WordSenseBuilder`, which also reads it to evaluate the dictionary and morphology rules with the same
resolver code. `VectorMath.Dot` is the only numeric helper the scoring uses. The unit also depends on the SenseRecord
unit: `LemmaEntry` validates the sense set and creates one `WordDetails` per `SenseDefinition`, and validates the
meaning and respelling of every sense through `SenseDefinition.Validate`, which is how `Read` rejects bad sense
strings.
