# SenseRecord Unit Design

## Overview

The SenseRecord unit holds the immutable description of a sense and the rules that derive tag-level answers
from sense scores. It consists of `WordSense`, `SenseDefinition`, `TagScorer`, `TextSplitter` and
`WordSenseToken`.

## Data Model

- `WordSense`: sealed record with `Lemma`, `Sense` and `Tags` (a frozen, interned
  `IReadOnlySet<string>`). `HasTag(tag)` tests membership. One instance exists per (lemma, sense), created
  when the table is loaded, so callers may compare by reference or by value.
- `SenseDefinition`: a sense label with its tags (1 to 16, ascending, no duplicates) as stored in the table. A
  tag starts with a lower-case ASCII letter, followed by lower-case letters, digits or `-`, at most 32
  characters.
- `WordSenseToken(Start, Length, Text, Sense)`: a tile of an input text.

## Methods

- `TagScorer.Classify(senses, scores, tags, threshold)`: tag score = best sense score among
  senses carrying the tag; senses with none of the queried tags are the "other" group. The best group wins;
  a tag wins ties against other and ties between tags go to the first tag in ordinal order. Result is `null`
  when no queried tag is on the lemma, other wins, or the margin is below the threshold. When only one group
  is present the margin is infinite.
- `TagScorer.IsTag(senses, scores, tag, threshold)`: `true`/`false` from the tag score versus other,
  `null` when the tag is absent or
  the margin is too small.
- `TextSplitter.SplitSegments(text)`: words (letters and digits with internal apostrophes and hyphens),
  whitespace runs and single other characters, tiling the text. `SplitWords` returns the non-space tiles.

## Interactions

Used by `WordSenseClassifier`, `WordSenseTable` and `LemmaModel`; has no dependencies on other units.
