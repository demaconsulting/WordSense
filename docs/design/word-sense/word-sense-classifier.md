# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which part of speech, and for a heteronym which
pronunciation, does this word have here".

## Data Model

- `WordDetails`, `Pronunciation`, `PartOfSpeech`: the answer values of the SenseRecord unit; one `WordDetails`
  instance per (lemma, sense) is created when the table is loaded.
- `WordAnalysis(Word, Details?, Margin, Abstained, RunnerUp?, RunnerUpMargin)`: the readonly record struct
  returned for every word of every call; see the SenseRecord unit for the field semantics. For a
  two-sense lemma the scores are `-a` and `+a`, so `Margin` is `2|a|`, `RunnerUp` is the other sense and
  `RunnerUpMargin` is 0. A tie gives `Margin` 0; of equal scores in a one-versus-rest lemma the sense that
  comes first in the table wins, and for a binary lemma an activation of exactly 0 selects the second sense.
  `Abstained` is true when `Margin` is below the lemma's abstain threshold (`LemmaModel.AbstainMargin` times
  `AbstainMarginScale`) by `AbstainRule`; `Details` is then null but the margin and runner-up are still reported.
  An unknown word (not a form of a lemma with a classifier) has null `Details`, `Abstained` false, no runner-up and
  zero margins, so it is distinguishable from an abstention.
- `WordSenseToken(Start, Length, Text, Analysis)`: a tile of the input text of the raw-string overload; the type is
  declared in `WordSenseClassifier.cs` next to `IWordSenseClassifier` and is owned by this unit. `Analysis` is the
  analysis of a word tile and is the unknown-word analysis for any other tile. Invariant: the tiles returned for a
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
- `ClassifyAsync(IReadOnlyList<string> words, ct)`: primary API. Returns a never-null array parallel to the input
  with one full `WordAnalysis` per word, always, so there is no separate detail query. Words are
  resolved to a lemma (lower-case, curly apostrophe normalized; a trailing `'s` is removed only when the word
  is not already a known form itself). The words are encoded in one pass, joined by single spaces.
- `ClassifyAsync(string text, ct)`: splits the text into tiles (words, whitespace runs and single other
  characters) that tile the input exactly and analyzes the words as above.
- `KnownLemmas`, `GetCandidates(lemma)`: describe the table; `GetCandidates` lists every possible answer of a
  lemma, in table order, and an empty list for an unknown lemma.
Texts longer than `MaxSequenceLength` use a window around each target instead of one pass.
Features are the mean of the target word pieces' hidden states minus the table mean, normalized
(`FeatureExtractor`, shared with the builder).

## Error Handling

- Null arguments (encoder, table, tokenizer, words, a word, text) raise `ArgumentNullException`; a null `options`
  is not an error and means the defaults.
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
- Unknown and abstained words are not errors; they are reported in the analysis.

## Interactions

Uses `WordPieceTokenizer`, `WordSenseTable`, `IWordSenseEncoder` and `WordSenseModelDownloader`, and the
SenseRecord unit (the answer types, `TextSplitter` and `AbstainRule`). The
classifier is thread-safe when its encoder is: the table and tokenizer are immutable and the session is
shared.
