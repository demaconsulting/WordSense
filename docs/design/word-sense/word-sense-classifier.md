# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which sense does this word have here".

## Data Model

- `WordSenseResult(Lemma, Label, Margin)` and `WordSenseToken(Start, Length, Text, Sense)`.
- `WordSenseClassifierOptions`: `ModelDirectory`, `Downloader`, `MaxSequenceLength` (256),
  `IntraOpThreads`, `AbstainMarginScale`.

## Methods

- `CreateAsync(options, ct)`: resolves the model directory (given, or downloaded and verified),
  verifies the files, creates tokenizer, encoder and table, and takes ownership of the encoder.
- Constructor: rejects null arguments and a table whose model digest or dimension differs from the
  encoder's (`WordSenseModelMismatchException`).
- `ClassifyAsync(text, ct)`: tiles the text into words, whitespace runs and single characters;
  resolves each word to a lemma (lower-case, curly apostrophe normalized, trailing `'s` removed); encodes
  once; returns the tiles with results for known words.
- `ClassifyAsync(text, start, length, ct)`: classifies one span; `null` when the span is not a known
  form, cannot be mapped to tokens, or the margin is below `AbstainMarginScale ×` the lemma threshold.
- `KnownLemmas`, `GetSenseLabels(lemma)`: describe the table.

Texts longer than `MaxSequenceLength` use a window around each target instead of one pass.
Features are the mean of the target word pieces' hidden states minus the table mean, normalized
(`FeatureExtractor`, shared with the builder).

## Interactions

Uses `WordPieceTokenizer`, `WordSenseTable`, `IWordSenseEncoder` and `WordSenseModelDownloader`. The
classifier is thread-safe: the table and tokenizer are immutable and the session is shared.
