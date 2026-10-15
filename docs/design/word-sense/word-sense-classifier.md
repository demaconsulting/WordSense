# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which sense does this word have here".

## Data Model

- `WordSense(Lemma, Sense, Tags)`: sealed immutable record, one instance per (lemma, sense) created
  when the table is loaded; see the SenseRecord unit.
- `WordSenseDetail(Sense, Margin, RunnerUp, RunnerUpMargin, Abstained)`: readonly struct.
- `WordSenseToken(Start, Length, Text, Sense)`: a tile of the input text of the raw-string overload.
- `WordSenseClassifierOptions`: `ModelDirectory`, `Downloader`, `MaxSequenceLength` (256),
  `IntraOpThreads`, `AbstainMarginScale`.

## Methods

- `CreateAsync(options, ct)`: resolves the model directory (given, or downloaded and verified),
  verifies the files, creates tokenizer, encoder and table, and takes ownership of the encoder.
- Constructor: rejects null arguments and a table whose model digest or dimension differs from the
  encoder's (`WordSenseModelMismatchException`).
- `ClassifyAsync(IReadOnlyList<string> words, ct)`: primary API. Returns an array parallel to the input
  with the `WordSense` of each known word, and `null` for unknown words and for abstentions. Words are
  resolved to a lemma (lower-case, curly apostrophe normalized, trailing `'s` removed). The words are
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

## Interactions

Uses `WordPieceTokenizer`, `WordSenseTable`, `IWordSenseEncoder` and `WordSenseModelDownloader`. The
classifier is thread-safe: the table and tokenizer are immutable and the session is shared.
