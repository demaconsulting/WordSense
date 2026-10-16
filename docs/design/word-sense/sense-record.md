# SenseRecord Unit Design

## Overview

The SenseRecord unit holds the immutable description of a sense and the rules that derive tag-level answers
from sense scores. It consists of `WordSense`, `SenseDefinition`, `TagScorer`, `TextSplitter` and
`WordSenseToken`.

## Data Model

- `WordSense`: sealed, immutable class (not a record, so it has no value equality) with `Lemma`, `Sense`,
  `Tags` (a frozen, ordinal `IReadOnlySet<string>`, never empty) and `Group` (never null or empty; defaults
  to the sense label; the senses of one lemma that share a pronunciation share a group, which is the library's
  only equivalence mechanism, as it holds no pronunciation data). Tags are true parts
  of speech plus qualifiers; a sense has one part of speech. `HasTag(tag)` tests membership ordinally and
  throws `ArgumentNullException` for a null tag. One instance exists per (lemma, sense), created when the
  owning `LemmaModel` is constructed, so callers compare senses by reference.
- `SenseDefinition`: a record with the sense label (`Name`), its tags (1 to `MaxTagsPerSense` = 16,
  ascending ordinal, no duplicates) and an optional `Group` (null means the sense label) as stored in the
  table. `IsValidTag(tag)` accepts a tag that starts with a lower-case ASCII letter, followed by
  lower-case letters, digits or `-`, at most `MaxTagLength` = 32 characters, with nothing after the last
  character (a trailing newline is rejected); null and empty strings are not valid. A group follows the same
  syntax. `Validate()` throws `ArgumentException` for an empty name, no tags or more than 16, an invalid,
  unsorted or duplicated tag, or an invalid group.
- `WordSenseToken(Start, Length, Text, Sense)`: a tile of an input text, owned by this unit and returned by the
  classifier's raw-string overload. `Sense` is `null` when the tile is not a known ambiguous word, or when the
  classifier abstained because its confidence was too low. Invariant: the tokens returned for a text tile it
  exactly; the tiles are in order with no gaps or overlaps, so concatenating every `Text` reproduces the input.
  The tiles come from `TextSplitter.SplitSegments`.

## Methods

- `TagScorer.Classify(senses, scores, tags, threshold)`: tag score = best sense score among
  senses carrying the tag; senses with none of the queried tags are the "other" group. The best group wins;
  a tag wins ties against other and ties between tags go to the first tag in ordinal order. Result is `null`
  when no queried tag is on the lemma, other wins, or the margin is below the threshold. When only one group
  is present the margin is infinite.
- `TagScorer.Abstains(margin, threshold)`: the single abstention predicate shared by every classification path;
  it is `!(margin >= threshold)`, so a NaN margin or a NaN threshold abstains.
- `TagScorer.IsTag(senses, scores, tag, threshold)`: `true`/`false` from the tag score versus other (the tag wins
  a tie, so equal scores give `true`), `null` when the tag is absent or the margin is too small.
- `TextSplitter.SplitSegments(text)`: words, whitespace runs and single other characters, tiling the
  text. A word is a run of letters, digits and combining marks (non-spacing and spacing), and may contain
  an internal apostrophe (`'` or U+2019) or hyphen that is directly followed by a word character, so
  `don't`, `well-known` and `x1` are each one word; a trailing apostrophe or hyphen is its own tile.
  Surrogate pairs are kept together as one character. `SplitWords` returns the non-space tiles.

## Interactions

Used by `WordSenseClassifier`, `WordSenseTable` and `LemmaModel`; has no dependencies on other units.
