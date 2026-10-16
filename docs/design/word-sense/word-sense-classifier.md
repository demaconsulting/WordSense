# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which sense does this word have here".

## Data Model

- `WordSense`: sealed immutable class, one instance per (lemma, sense) created when the table is
  loaded, with `Lemma`, `Sense`, `Tags` and `Group`; see the SenseRecord unit.
- `WordSenseDetail(Sense, Margin, RunnerUp, RunnerUpMargin, Abstained)`: readonly struct.
- `WordSenseToken(Start, Length, Text, Sense)`: a tile of the input text of the raw-string overload (the type
  is owned by the SenseRecord unit; see the SenseRecord design).
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
- `ClassifyDetailAsync(words, ct)`: as above, returning a `WordSenseDetail?` per word; the best sense is
  reported also when `Abstained` is true. `Margin` is best minus runner-up; `RunnerUpMargin` is runner-up
  minus third (0 for two senses).
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

Uses `WordPieceTokenizer`, `WordSenseTable`, `IWordSenseEncoder` and `WordSenseModelDownloader`. The
classifier is thread-safe when its encoder is: the table and tokenizer are immutable and the session is
shared.
