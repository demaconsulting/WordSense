# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which sense does this word have here".

## Data Model

- `WordSense`: sealed immutable class, one instance per (lemma, sense) created when the table is
  loaded, with `Lemma`, `Sense`, `Tags` and `Group`; see the SenseRecord unit.
- `WordSenseDetail(Sense, Margin, RunnerUp, RunnerUpMargin, Abstained)`: readonly record struct, declared in
  `WordSenseClassifier.cs` and returned by `ClassifyDetailAsync`. `Sense` is the best sense and is set even when the
  classifier abstained. `Margin` is the score of the best sense minus the score of the runner-up (a score
  difference, not a probability, comparable only within one lemma; for a two-sense lemma the scores are `-a` and
  `+a`, so the margin is `2|a|`). `RunnerUp` is the second-best sense and is never null because every lemma has at
  least two senses; a tie gives `Margin` 0. Of equal scores in a one-versus-rest lemma the sense that comes first in
  the table wins; for a binary lemma an activation of exactly 0 selects the second sense.
  `RunnerUpMargin` is the runner-up score minus the third-best score, or 0 for a two-sense lemma. `Abstained` is
  true when `Margin` is below the lemma's abstain threshold (`LemmaModel.AbstainMargin` times
  `AbstainMarginScale`), by the same predicate `ClassifyAsync` uses. The threshold itself is not part of the
  detail; a caller who needs it derives it from the table.
- `WordSenseToken(Start, Length, Text, Sense)`: a tile of the input text of the raw-string overload; the type is
  declared in `WordSenseClassifier.cs` next to `IWordSenseClassifier` and is owned by this unit. `Sense` is `null`
  when the tile is not a known ambiguous word, or when the classifier abstained. Invariant: the tiles returned for a
  text are in order with no gaps or overlaps, so concatenating every `Text` reproduces the input; they come from
  `TextSplitter.SplitSegments`.
- `IWordSenseClassifier`: the public interface, declared in `WordSenseClassifier.cs`, implemented by
  `WordSenseClassifier`.
- `WordSenseClassifierOptions`: `ModelDirectory`, `Downloader`, `MaxSequenceLength` (256),
  `IntraOpThreads`, `AbstainMarginScale`.

## Methods

- `CreateAsync(options, ct)`: resolves the model directory (given, or downloaded and verified),
  verifies the files, creates tokenizer, encoder and table, and takes ownership of the encoder.
- Constructor: rejects null arguments and a table whose model digest or dimension differs from the
  encoder's (`WordSenseModelMismatchException`). The overload that takes a `vocabularyPath` builds the tokenizer
  from that vocabulary file instead of a supplied tokenizer.
- `ClassifyAsync(IReadOnlyList<string> words, ct)`: primary API. Returns an array parallel to the input
  with the `WordSense` of each known word, and `null` for unknown words and for abstentions. Words are
  resolved to a lemma (lower-case, curly apostrophe normalized; a trailing `'s` is removed only when the word
  is not already a known form itself). The words are
  encoded in one pass, joined by single spaces.
- `ClassifyDetailAsync(words, ct)`: as above, returning a `WordSenseDetail?` per word (null for a word that has no
  classifier); the best sense is
  reported also when `Abstained` is true. `Margin` is best minus runner-up; `RunnerUpMargin` is runner-up
  minus third (0 for two senses); see the data model for the full semantics.
- `ClassifyTagAsync(words, tags, ct)` and `IsTagAsync(words, tag, ct)`: tag-level queries. A tag's score
  is the best sense score among the senses carrying it; the senses without any queried tag form an
  "other" group. The tag with the best score is returned, and `null` when no queried tag is on the lemma,
  other wins, or the margin is below the threshold. `IsTagAsync` returns `true`/`false`, or `null` when
  the tag is absent from the lemma or the margin is too small. The tag-level threshold is the lemma's
  abstain threshold (derived, not stored).
- `ClassifyAsync(string text, ct)`: splits the text into tiles (words, whitespace runs and single other
  characters) that tile the input exactly and classifies the words as above.
- `KnownLemmas`, `GetSenses(lemma)`: describe the table.
Texts longer than `MaxSequenceLength` use a window around each target instead of one pass.
Features are the mean of the target word pieces' hidden states minus the table mean, normalized
(`FeatureExtractor`, shared with the builder).

## Error Handling

- Null arguments (encoder, table, tokenizer, words, a word, text, lemma, tags, a tag) raise
  `ArgumentNullException`; a null `options` is not an error and means the defaults. An empty tag list or an
  empty tag raises `ArgumentException`.
- `MaxSequenceLength` outside 8 to 512, a negative or NaN `AbstainMarginScale` and a negative `IntraOpThreads` raise
  `ArgumentOutOfRangeException` (positive infinity is accepted for `AbstainMarginScale`, so every word abstains).
  The constructor checks the first two through `ValidateOptions`; `CreateAsync` runs the same validation and also
  checks `IntraOpThreads`, all before it downloads or verifies any model file. A scale of 0 never abstains.
- The constructor that takes a `vocabularyPath` reads the vocabulary through `WordPieceTokenizer.FromFile`:
  a missing file raises `FileNotFoundException`, another read failure `IOException`, and a vocabulary without the
  `[UNK]`, `[CLS]` or `[SEP]` token raises `ArgumentException`.
- A table whose model digest or dimension differs from the encoder's raises
  `WordSenseModelMismatchException`.
- A user-supplied `ModelDirectory` is verified against the pinned digests before use; a missing,
  altered or incomplete file raises an exception from `WordSenseModelLocation.FromDirectory`.
  If creating the encoder or classifier fails after the encoder was opened, the encoder is disposed.
- After `Dispose`, a classifier that owns its encoder throws `ObjectDisposedException` from the
  encoder on the next classification; a classifier built over a borrowed encoder leaves it open.
- A cancelled token raises `OperationCanceledException` before encoding starts and between targets.

## Interactions

Uses `WordPieceTokenizer`, `WordSenseTable`, `IWordSenseEncoder` and `WordSenseModelDownloader`, and the
SenseRecord unit (`WordSense`, `TextSplitter` and `TagScorer`). The
classifier is thread-safe when its encoder is: the table and tokenizer are immutable and the session is
shared.
