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
and 2 tables are rejected. Each sense becomes one shared `WordSense` record when the table is read, and
the records of equal tags share the table's tag strings.

## Methods

- `LoadEmbedded()`, `Read(bytes)`: parse and validate; any truncation, bad magic, bad version,
  inconsistent count or digest mismatch raises `WordSenseTableFormatException`.
- `ToArray()`, `Write(stream)`: deterministic serialization.
- `TryGetLemma`, `LemmaModel.Score(feature)`: best sense index and margin.

## Error Handling

- The `WordSenseTable` and `LemmaModel` constructors throw `ArgumentNullException` for null arguments
  and `ArgumentException` for inconsistent content: a model SHA-256 that is not 64 hexadecimal
  characters, a mean that is not finite or does not match the dimension, lemma models that are
  duplicated, unordered or of another dimension, forms that name a lemma without a model, fewer than two
  senses (exactly two for binary), weight or bias shapes that do not match, non-finite values,
  duplicate sense labels, and invalid sense definitions.
- `Write` throws `InvalidOperationException` when a string exceeds 65,535 UTF-8 bytes.
- `Read` verifies the trailing digest before trusting any length field, caps every count (dimension
  4096, lemmas 100,000, forms 2,000,000, senses per lemma 64, distinct tags 256), and checks the
  structure: tags valid, sorted and unique, sense tag indexes in range and ascending, every dictionary
  tag used, known model kinds, valid form-to-lemma indexes without duplicates, and no trailing data.
  Constructor validation failures raised while reading are reported as `WordSenseTableFormatException`.

## Interactions

Read by `WordSenseClassifier`; written by `WordSenseBuilder`. `VectorMath.Dot` is the only helper.
