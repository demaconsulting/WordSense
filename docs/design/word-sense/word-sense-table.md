# WordSenseTable Unit Design

## Overview

`WordSenseTable` is the one vocabulary of the library, and it holds no model data. It holds every lemma as a
`LemmaEntry`, a dictionary from lower-case word form to lemma, the enabled morphology rules, and the single resolver
that maps a surface word to a lemma entry. It is stored as one embedded resource, `wordsense-lexicon.bin` (format
`WSNS`). The trained classifiers of a model live in a separate table of the same unit, `WordSenseWeightsTable` (format
`WSNW`), one per model, which names the model and the lexicon it was trained for.

The unit contains `WordSenseTable.cs`, `WordSenseWeightsTable.cs`, `LemmaEntry.cs` (with `LemmaModelKind`),
`SenseWeights.cs`, `LexicalIndex.cs`, `LemmaMorphology.cs`, `LemmaResolution.cs`, `TableReader.cs`, `VectorMath.cs` and
`WordSenseTableFormatException.cs`. They are facets of one lookup structure whose only job is "surface form to lemma
entry, and lemma to trained weights", so they form one unit.

## Data Model

The uniform model is: surface form, lemma, `LemmaEntry`. A `LemmaEntry` is a lemma with a list of senses
(`SenseDefinition`: part of speech, optional meaning, optional respelling) and a flag, `IsContextual`.

- A **dictionary word** is an entry with one or more senses, none with a meaning, so it has no pronunciation. A
  dictionary word with several parts of speech is an entry with one sense per part of speech.
- A **context lemma** (a lemma that a model can decide, for example `record`) is a contextual entry with at least two
  senses. The order of its senses defines the order of the weight rows in every model's weights table.
- There is no other kind of entry. The entry decides what happens to a word: a contextual entry needs a model to
  choose; any other entry is answered from the lexicon alone.

Entry invariants, checked by the `LemmaEntry` constructor and again by the reader:

1. At least one sense; a (part of speech, meaning) pair is unique.
2. A contextual entry has at least two senses.
3. A non-contextual entry carries no meaning, so `IsHeteronym` is false.
4. Either all senses carry a meaning or none does; a heteronym has at least two distinct meanings; one meaning has
   one respelling; distinct meanings have distinct respellings.
5. A lemma appears once, and a form never spells another lemma (a single namespace).

`SenseWeights` holds the mathematics of one lemma's classifier and is stored in the weights table: `Kind`
(`LemmaModelKind.Binary`: one weight row, two senses, margin `2|a|`; `OneVersusRest`: one row per sense, margin between
the best and the second-best score), `Weights`, `Bias`, `AbstainMargin` and `Dimension`. `Scores(feature, scores)`
fills one score per sense (`{-a, +a}` for a binary kind with activation `a`); `Score(feature)` returns the best sense
and the margin.

`LemmaEntry.Candidates` is one `WordDetails` per sense, in the order of `Senses`, built when the entry is
constructed; the answer a sense stands for is `(lemma, part of speech, pronunciation if any)`. The instances are
shared per entry instance. Context entries live for the life of the table, so their candidates are reference-equal on
every access. A dictionary entry is created on demand for each lookup (no cache, because a cache keyed by untrusted
input would grow without bound), so two lookups give equal but not reference-equal candidates.

### Physical stores

Two private stores sit behind the one public surface:

- The context entries (a few hundred) are kept as objects in an ordinal-ordered list with a name index.
- The dictionary words (over a hundred thousand) are compiled into a `LexicalIndex`: a sorted set of lower-case `a-z`
  words, each with a mask of its parts of speech (bit `i` is `PartOfSpeech` value `i`), kept as one front-coded byte
  array. Nothing is expanded into per-word objects. Records are in blocks of 32 words; a block starts with a shared
  prefix length of 0. A lookup binary searches the block head words in place and decodes at most 32 records without
  allocating.

The public constructor takes every entry in one list, contextual or not, and partitions them; callers and the
builder never see two tables.

### Binary layout of the lexicon table

Little-endian: magic `WSNS`, u16 format version (6), u16 flags (the enabled `LemmaMorphologyRules`), u32 context
lemma count, and per context lemma in ordinal order: name, u16 sense count and the senses (u8 part of speech, meaning
string, respelling string; an empty string means "none"). After the lemmas come the u32 form count and the forms
(string and u32 context-lemma index). Then follows the dictionary section: u32 word count, u16 block size (32), u32
decoded length, u32 compressed length and the raw-DEFLATE compression of the front-coded records (u8 shared prefix
length, u8 suffix length, the suffix, u8 part-of-speech mask). A trailing SHA-256 digest covers all preceding bytes.
The writer emits lemmas, forms and words in ordinal order, so output is deterministic for a given runtime; the
compressed bytes of the dictionary section can differ between runtimes, so consumers that need identity compare the
decoded content. The reader requires lemmas in ordinal order and accepts forms in any order. Strings are a u16 byte
length followed by UTF-8.

The lexicon table holds no model identity, no embedding dimension, no mean and no weights. Format version 6 is the only
version and any other version is rejected. A sense label is a build-time join key and is not stored. The table is about
0.43 MB.

`WordSenseTable.Sha256` is the digest of the serialized table; a weights table names it so that weights are only used
with the lexicon they were trained for.

### Binary layout of the weights table

Little-endian: magic `WSNW`, u16 format version (1), the model id (string), the 32-byte SHA-256 of the model file, the
32-byte SHA-256 of the lexicon table, u32 dimension, f32 mean vector (the anchor mean used for centering), u32 lemma
count, and per lemma in ordinal order: name, u16 sense count, u8 kind, the weights, the biases and the f32 abstention
margin. A trailing SHA-256 digest covers all preceding bytes. The weights table has one entry for each context lemma
of the lexicon it names and for no other lemma; the sense count of each entry equals the sense count of the lemma.
Format version 1 is the only version and any other version is rejected.

## Resolution

`TryResolve(word, useDictionary, out LemmaResolution)` is the only way the classifier turns a surface word into an
entry. The word is lower-cased and a typographic apostrophe (U+2019) is straightened. The first rule that succeeds
wins:

1. **Form map**: an explicit inflection of a context lemma (for example `recorded` gives `record`).
2. **Exact lemma**: a context lemma, then (when `useDictionary`) a dictionary word.
3. **Possessive** `'s`: the stem is resolved by rules 1 and 2, accepted only for a contextual entry.
4. **Gated morphology**, only when `useDictionary` is true and rules 1 to 3 failed, that is, only for a word the table
   does not contain and that has more than three letters (shorter words are never derived):
   `LemmaMorphology` derives candidate base spellings from the suffix, looks each base up as in rule 2 and requires
   the base to be a dictionary entry (not contextual) that satisfies the rule. The base's `SenseIndex` is
   authoritative for which sense applies, and each rule shapes the entry differently: a plural returns a one-sense
   entry of the base; a past tense returns the full entry of the base, narrowed through `LemmaResolution.SenseIndex`
   to its verb sense; an adverb returns a synthesized one-sense Adverb entry for the base lemma. The plural rule never
   applies to a word ending in `-ss`, `-us` or `-is`. A resolution with a sense count of 1 or a narrowed `SenseIndex`
   is answered directly by the general flow.

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
rule never overrides a word the table contains and never applies to a contextual entry, so morphology cannot create or
alter a context answer. `LemmaResolution(Entry, SenseIndex)` is an internal record struct; `SenseIndex` is -1 when
every sense applies. The lemma reported to callers is the lemma of the entry (the base for a form or a derived word).

## Public Interface

- `WordSenseTable` properties: `Sha256` (lower-case hexadecimal digest of the serialized table), `Count` (every
  lemma), `ContextLemmas` (contextual lemmas, ordinal order), `Lemmas` (every lemma in ordinal order, dictionary
  lemmas decoded one at a time as the sequence is enumerated), `Forms` (form to context lemma) and `Morphology` (the
  enabled rules). Callers must treat these collections as read-only; the constructors copy the lemma list they are
  given but not the forms map.
- `LemmaEntry` properties: `Lemma`, `Senses`, `Candidates`, `IsHeteronym` and `IsContextual`.
- `WordSenseWeightsTable` properties: `ModelId`, `ModelSha256` and `LexiconSha256` (lower-case hexadecimal),
  `Dimension`, `Mean` (length `Dimension`) and `Lemmas` (the lemmas it covers, ordinal order). `TryGet(lemma,
  out weights)` returns the `SenseWeights` of a lemma.
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

- `WordSenseTable.LoadEmbedded()`, `Read(bytes)`: parse and validate; any truncation, bad magic, bad version,
  inconsistent count or digest mismatch raises `WordSenseTableFormatException`.
- `WordSenseTable.ToArray()`, `Write(stream)`: serialization as described above.
- `TryGetLemma(lemma, out entry)`: true with the entry for any lemma of the table, contextual or not, false for
  an unknown one; throws `ArgumentNullException` for a null lemma. It takes the lemma itself, not an inflected form.
- `WordSenseWeightsTable.Read(bytes)`, `ToArray()`: parse, validate and serialize the weights table; `ComputeSha256()`
  gives the digest of its serialized form.
- `WordSenseWeightsTable.EnsureMatches(table)`: throws `WordSenseModelMismatchException` when the lexicon digest named
  by the weights differs from `table.Sha256`, when the weights do not cover exactly the context lemmas of the table, or
  when the sense count of a lemma differs.
- `SenseWeights.Score(feature)`: best sense index and margin; it throws `ArgumentException` when the feature length
  differs from the dimension. `SenseWeights.Scores(feature, scores)` throws `ArgumentException` when the feature
  length differs from the dimension or the scores length from the sense count.
- `LexicalIndex.TryFind(word, out mask)`: allocation-free lookup; anything but 1 to 255 letters `a-z` is simply not
  found.

## Error Handling

- The `WordSenseTable`, `WordSenseWeightsTable`, `LemmaEntry` and `SenseWeights` constructors throw
  `ArgumentNullException` for null arguments and `ArgumentException` for inconsistent content: a SHA-256 that is not
  64 hexadecimal characters, a mean that is not finite or does not match the dimension, lemmas that are duplicated,
  unordered or of another dimension, forms that name a lemma that is not contextual, a form that spells another
  lemma, a dictionary lemma that is not 1 to 255 letters `a-z`, a contextual lemma with fewer than two senses
  (exactly two for a binary kind), weight or bias shapes that do not match, non-finite values, and an invalid sense
  set (an undefined part of speech, a malformed meaning or respelling, meanings given to only some senses, meanings
  on a non-contextual lemma, a heteronym with fewer than two distinct meanings, a repeated part of speech and
  meaning, inconsistent respellings of one meaning, or distinct meanings that share a respelling). `SenseWeights` also
  rejects a kind that is not defined (`ArgumentOutOfRangeException`), and a null element of the sense list or of the
  lemma list raises `ArgumentNullException`.
- `Write` throws `InvalidOperationException` when a string exceeds 65,535 UTF-8 bytes, and `Write` and `ToArray`
  throw `OverflowException` when a count does not fit its field (for example more than 65,535 senses in
  one lemma).
- `WordSenseTable.Read` verifies the trailing digest before trusting any length field, caps every count (context
  lemmas 100,000, forms 2,000,000, senses per context lemma 2 to 64, dictionary words 400,000 and 4 MB decoded
  dictionary), inflates the dictionary only up to its declared decoded length (a guard against decompression bombs)
  and checks the structure: a defined part of speech for every sense, defined morphology rules, valid form-to-lemma
  indexes (in range and without a duplicated form), strings that are valid UTF-8, no trailing data, and in the
  dictionary strictly ascending words of letters `a-z`, a block head with prefix 0, a prefix no longer than the
  previous word, suffix lengths in range, a non-zero part-of-speech mask (the mask is one byte and all eight bits are
  defined parts of speech, so no bit can be undefined), an exact decoded length and no word that is also a context
  lemma or a form. Every failure, including a constructor validation failure raised while reading, is reported as
  `WordSenseTableFormatException`.
- `WordSenseWeightsTable.Read` verifies the trailing digest first, caps the counts (dimension 4096, lemmas 100,000,
  senses per lemma 64), requires lemmas in ordinal order, known kinds and finite values, and reports every failure
  as `WordSenseTableFormatException`. Reading a lexicon table as a weights table, or the reverse, fails on the magic.
- The read caps are enforced only by `Read`: the constructors and `Write` do not apply them, so a table
  built in memory can exceed a cap and be written, but `Read` will then reject it.
- A table with no dictionary words is valid (a small test table); the dictionary section then has a word count of 0.

## Interactions

Read by `WordSenseClassifier`, which calls `TryResolve`, `TryGetLemma` and `ContextLemmas` on the lexicon table and
`TryGet` and the mean on the weights table; written by `WordSenseBuilder`, which writes one lexicon table and one
weights table per model and also reads the lexicon table to evaluate the dictionary and morphology rules with the same
resolver code. `VectorMath.Dot` is the only numeric helper the scoring uses. The MiniLM package embeds a weights table
and loads it with `WordSenseWeightsTable.Read`. The unit also depends on the SenseRecord unit: `LemmaEntry` validates
the sense set and creates one `WordDetails` per `SenseDefinition`, and validates the meaning and respelling of every
sense through `SenseDefinition.Validate`, which is how `Read` rejects bad sense strings. `WordSenseWeightsTable` raises
`WordSenseModelMismatchException`, which belongs to the WordSenseModel unit.
