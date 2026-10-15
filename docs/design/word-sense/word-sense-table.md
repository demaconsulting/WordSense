# WordSenseTable Unit Design

## Overview

`WordSenseTable` holds everything the classifier needs besides the model: the model identity and
SHA-256, the embedding dimension, the anchor mean used for centering, one `LemmaModel` per lemma,
and a dictionary from lower-case word form to lemma. It is stored as an embedded resource,
`wordsense-table.bin`.

## Data Model

Binary layout, little-endian: magic `WSNS`, format version, model id, model SHA-256, dimension, mean
vector, tag dictionary, lemma count, per lemma (name, kind, per sense a label and ascending tag
indexes, weights, biases, abstention margin), form
count, forms, and a trailing SHA-256 digest of all preceding bytes.

`LemmaModel` kinds: binary (one weight row, two senses, margin `2|s|`) and one-versus-rest (one row
per sense, margin between the best and the second-best score).

Format version 2 adds the tag dictionary and per-sense tags; version 1 tables are rejected. Each sense becomes
one shared `WordSense` record when the table is read, and tag sets are interned across senses.

## Methods

- `LoadEmbedded()`, `Read(bytes)`: parse and validate; any truncation, bad magic, bad version,
  inconsistent count or digest mismatch raises `WordSenseTableFormatException`.
- `ToArray()`, `Write(stream)`: deterministic serialization.
- `TryGetLemma`, `LemmaModel.Score(feature)`: best sense index and margin.

## Interactions

Read by `WordSenseClassifier`; written by `WordSenseBuilder`. `VectorMath.Dot` is the only helper.
