# SenseRecord Unit Design

## Overview

The SenseRecord unit holds the immutable description of a sense and the rules that derive tag-level answers
from sense scores. It consists of `WordSense`, `SenseDefinition`, `TagScorer` and `TextSplitter`.

## Data Model

- `WordSense`: sealed, immutable class (not a record, so it has no value equality) with `Lemma`, `Sense`,
  `Tags` (a frozen, ordinal `IReadOnlySet<string>`, never empty) and `Group` (never null or empty; defaults
  to the sense label; the senses of one lemma that share a pronunciation share a group, which is the library's
  only equivalence mechanism, as it holds no pronunciation data). Tags are true parts
  of speech plus qualifiers. That a sense has exactly one part of speech is a data convention that this unit
  does not enforce (a `WordSense` or `SenseDefinition` accepts any valid tag set); the builder's part-of-speech
  rules enforce it when a table is built, and the embedded-table test
  `WordSense_EmbeddedTable_EverySenseHasExactlyOnePartOfSpeech` checks it on the shipped table. `HasTag(tag)`
  tests membership ordinally and
  throws `ArgumentNullException` for a null tag. One instance exists per (lemma, sense), created when the
  owning `LemmaModel` is constructed, so callers compare senses by reference.
- `SenseDefinition`: a record with the sense label (`Name`), its tags (1 to `MaxTagsPerSense` = 16,
  ascending ordinal, no duplicates) and an optional `Group` (null means the sense label) as stored in the
  table. `IsValidTag(tag)` accepts a tag that starts with a lower-case ASCII letter, followed by
  lower-case letters, digits or `-`, at most `MaxTagLength` = 32 characters, with nothing after the last
  character (a trailing newline is rejected); null and empty strings are not valid. A group follows the same
  syntax. `Validate()` throws `ArgumentException` for an empty name, no tags or more than 16, an invalid,
  unsorted or duplicated tag, or an invalid group.

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

## Error Handling

- `SenseDefinition.Validate` throws `ArgumentException` for an invalid label, group, tag set or tag.
- `WordSense.HasTag` throws `ArgumentNullException` for a null tag.
- `TagScorer` and `TextSplitter` do not throw for well-formed input; an abstention is a `null`
  result, not an error.

## Interactions

Used by `WordSenseClassifier`, `WordSenseTable` and `LemmaModel` (the `WordSense` and `SenseDefinition` types);
has no dependencies on other units. `TagScorer` and `TextSplitter` are internal helpers used by
`WordSenseClassifier` and by `WordSenseBuilder` (the builder reaches them through the assembly's
internals-visible declaration). The `WordSenseToken` records that the classifier returns are built from the
`TextSplitter` segments; the type belongs to the classifier unit.
