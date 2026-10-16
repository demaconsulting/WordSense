# WordSenseTable Unit Design

## Overview

`WordSenseTable` holds everything the classifier needs besides the model: the model identity and
SHA-256, the embedding dimension, the anchor mean used for centering, one `LemmaModel` per lemma,
and a dictionary from lower-case word form to lemma. It is stored as an embedded resource,
`wordsense-table.bin`.

## Data Model

Binary layout, little-endian: magic `WSNS`, u16 format version, a reserved u16 flags field (written as
zero and ignored when read), model id, model SHA-256, u32 dimension, f32 mean vector, tag dictionary
(u16 count, ordinal-sorted tag strings), u32 lemma count, and per lemma: name, u16 sense count, the
senses (label, u8 tag count, ascending u16 tag indexes, group string), then the u8 model kind, weights,
biases and the f32 abstention margin. After the lemmas come the u32 form count, the forms (string and
u32 lemma index), and a trailing SHA-256 digest of all preceding bytes. Strings are a u16 byte length
followed by UTF-8.

`LemmaModel` kinds: binary (one weight row, two senses, margin `2|s|`) and one-versus-rest (one row
per sense, margin between the best and the second-best score). `LemmaModel.Scores(feature, scores)`
fills one score per sense (`{-a, +a}` for a binary model with activation `a`); `Score(feature)` returns
the best sense and the margin.

Format version 3 adds a per-sense group string (empty = the sense label) after the tag indexes; version 1
and 2 tables are rejected. Each sense becomes one shared `WordSense` record when its `LemmaModel` is
constructed (so for a read table during `Read`, and for a built table in the builder), and
the records of equal tags share the table's tag strings.

## Public Interface

- `WordSenseTable` properties: `ModelId`, `ModelSha256` (lower-case hexadecimal), `Dimension`, `Mean` (length
  `Dimension`), `Lemmas` (ordinal order) and `Forms` (form to lemma). Callers must treat these collections and the
  arrays of a `LemmaModel` as read-only; the constructors copy the arrays and the lemma list they are given but not
  the forms map.
- `LemmaModel` properties: `Lemma`, `Senses` (one shared `WordSense` per sense, in weight-row order), `Kind`
  (`LemmaModelKind.Binary` or `OneVersusRest`; any other value is rejected with `ArgumentOutOfRangeException`),
  `Weights`, `Bias`, `AbstainMargin` and `Dimension`.
- `WordSenseTableFormatException` is the only exception `Read` and `LoadEmbedded` report for bad content.

## Scoring Algorithm

`VectorMath.Dot(left, right)` sums the products over the length of `left`; `right` must be at least as long.
It uses `Vector<float>` SIMD lanes for full blocks and a scalar loop for the remainder, so any dimension gives
the same result as the plain scalar sum up to floating-point rounding. A binary model computes one activation
`a = dot(weights, feature) + bias`; the scores are `{-a, +a}`, and the tie rule is that an activation of 0 or more
(`a >= 0`) selects sense index 1. A one-versus-rest model scores each row and the first highest row wins a tie.

## Methods

- `LoadEmbedded()`, `Read(bytes)`: parse and validate; any truncation, bad magic, bad version,
  inconsistent count or digest mismatch raises `WordSenseTableFormatException`.
- `ToArray()`, `Write(stream)`: deterministic serialization.
- `TryGetLemma(lemma, out model)`: true with the model for a known lemma, false for an unknown one;
  throws `ArgumentNullException` for a null lemma. `LemmaModel.Score(feature)`: best sense index and margin;
  it throws `ArgumentException` when the feature length differs from the dimension. `LemmaModel.Scores(feature, scores)`
  throws `ArgumentException` when the feature length differs from the dimension or the scores length from the
  sense count.

## Error Handling

- The `WordSenseTable` and `LemmaModel` constructors throw `ArgumentNullException` for null arguments
  and `ArgumentException` for inconsistent content: a model SHA-256 that is not 64 hexadecimal
  characters, a mean that is not finite or does not match the dimension, lemma models that are
  duplicated, unordered or of another dimension, forms that name a lemma without a model, fewer than two
  senses (exactly two for binary), weight or bias shapes that do not match, non-finite values,
  duplicate sense labels, and invalid sense definitions. A `LemmaModel` also rejects a model kind that is not
  defined (`ArgumentOutOfRangeException`), and a null element of the sense list or of the lemma list raises
  `ArgumentNullException`.
- `Write` throws `InvalidOperationException` when a string exceeds 65,535 UTF-8 bytes, and `Write` and `ToArray`
  throw `OverflowException` when a count does not fit its field (for example more than 65,535 distinct tags or
  senses in one lemma).
- `Read` verifies the trailing digest before trusting any length field, caps every count (dimension
  4096, lemmas 100,000, forms 2,000,000, senses per lemma 2 to 64, distinct tags 256, tags per sense 1 to 16),
  and checks the
  structure: tags valid, sorted and unique, sense tag indexes in range and ascending, every dictionary
  tag used, known model kinds, a sense group that is empty or a valid group string, valid form-to-lemma indexes
  (in range and without a duplicated form), and no trailing data.
  A sense with zero or more than 16 tags and a sense whose group string is invalid are reported as
  `WordSenseTableFormatException`, like every other structural failure.
  Constructor validation failures raised while reading are reported as `WordSenseTableFormatException`.
- The read caps are enforced only by `Read`: the constructors and `Write` do not apply them, so a table
  built in memory can exceed a cap and be written, but `Read` will then reject it.

## Interactions

Read by `WordSenseClassifier`; written by `WordSenseBuilder`. `VectorMath.Dot` is the only numeric helper the
scoring uses. `LemmaModel` also depends on the SenseRecord unit: it creates one `WordSense` per
`SenseDefinition`, and `Read` uses `SenseDefinition.IsValidTag` to validate tags and groups.
