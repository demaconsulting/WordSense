# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows an encoder, a tokenizer and a table and answers "which part of speech, and for a heteronym which
pronunciation, does this word have here".

## Data Model

- `WordDetails`, `Pronunciation`, `PartOfSpeech`, `AnswerBasis`: the answer values of the SenseRecord unit; one
  `WordDetails` instance per sense of a context lemma is created when the table is loaded.
- `WordAnalysis(Word, Details?, Margin, Abstained, RunnerUp?, RunnerUpMargin, Basis, Candidates)`: the readonly record
  struct returned for every word of every call; see the SenseRecord unit for the field semantics and the table of
  cases. For a two-sense lemma with weights the scores are `-a` and `+a`, so `Margin` is `2|a|`, `RunnerUp` is the
  other sense and `RunnerUpMargin` is 0. A tie gives `Margin` 0; of equal scores in a one-versus-rest lemma the sense
  that comes first in the table wins, and for a binary lemma an activation of exactly 0 selects the second sense.
  `Abstained` is true for a context word when `Margin` is below the lemma's abstain threshold
  (`SenseWeights.AbstainMargin` times `AbstainMarginScale`) by `AbstainRule`; `Details` is then null but the margin
  and runner-up are still reported. A known weighted word that has no word pieces, or whose feature vector has zero
  norm, cannot be scored and yields the unknown result (basis `None`, null `Details`, `Abstained` false, zero margins,
  no candidates).
- `WordSenseToken(Start, Length, Text, Analysis)`: a tile of the input text of the raw-string overload; the type is
  declared in `WordSenseClassifier.cs` next to `IWordSenseClassifier` and is owned by this unit. `Analysis` is the
  analysis of a word tile and is the unknown-word analysis for any other tile. Invariant: the tiles returned for a
  text are in order with no gaps or overlaps, so concatenating every `Text` reproduces the input; they come from
  `TextSplitter.SplitSegments`.
- `IWordSenseClassifier`: the public interface, declared in `WordSenseClassifier.cs`, implemented by
  `WordSenseClassifier`. It exposes `ContextLemmas` (the lemmas that have a trained classifier, sorted) and
  `GetCandidates(lemma)`.
- `WordSenseClassifierOptions`: `ModelDirectory`, `Downloader`, `MaxSequenceLength` (256),
  `IntraOpThreads`, `AbstainMarginScale` and `UseLexicon` (default true).

## The Single Flow

Every word, whatever kind, takes the same path. There is one resolver and one decision rule:

1. **Resolve**: `table.TryResolve(word, UseLexicon, out resolution)` maps the surface word to a lemma entry (form
   map, exact lemma, possessive of a context lemma, gated morphology; see the table unit). No other lookup exists.
2. **Case policy** (only for an entry without weights): a dictionary entry is used when the word is lower case, or is
   the first word of the input, or directly follows a token that is exactly `.`, `!`, `?` or `:` or an opening quote or
   bracket, or is exactly `I`; otherwise the word is treated as unknown. A capitalized word elsewhere is usually a
   name, and measurement showed that treating those as dictionary words was wrong far too often. Entries with weights
   are not subject to the policy, so the behaviour of context lemmas is unchanged. For `ClassifyAsync(words)` the
   previous token is `words[i-1]`; for the text overload it is the previous non-space tile.
3. **Decide** from the number of senses `n` (1 when morphology narrowed the entry, otherwise the number of senses of
   the entry) and the weights:

   | Entry | Action | Basis |
   | --- | --- | --- |
   | Not found, or rejected by the case policy | unknown, no candidates | `None` |
   | `n == 1` | answer that sense directly, no encoder | `Dictionary` |
   | `n > 1` with weights | encode and score; answer, or abstain below the threshold | `Context` |
   | `n > 1` without weights | no answer, `Abstained` set, candidates are the senses | `Dictionary` |

   A lexicon word is just an entry with one sense and no weights. There is no special-case code for dictionary
   words except the case policy.
4. **Encode only when needed**: the classifier first resolves every word, then runs the tokenizer and the encoder only
   if at least one word resolved to an entry with weights. Dictionary answers, candidate-only words and unknown words
   never reach the tokenizer or the encoder, so a sentence made only of such words costs no inference. When the
   encoder does run, it runs once for the sentence when it fits one window (a window around each context target for
   long texts), and only the
   context words are scored; dictionary words in the same call are answered from the table.

`UseLexicon = false` makes entries without weights invisible to the resolver (no dictionary lookup, no morphology), so
the classifier answers exactly the context lemmas and their forms; words derived by morphology are therefore unknown
and `GetCandidates` of a dictionary-only lemma is empty. The answer `Basis` is `Context` when the lemma's classifier
decided, `Dictionary` when the table alone decided and `None` for an unknown word.

## Methods

- `CreateAsync(options, ct)`: resolves the model directory (given, or downloaded and verified),
  verifies the files, creates tokenizer, encoder and table, and takes ownership of the encoder.
- Constructor: rejects null arguments and a table whose model digest or dimension differs from the
  encoder's (`WordSenseModelMismatchException`). The overload that takes a `vocabularyPath` builds the tokenizer
  from that vocabulary file instead of a supplied tokenizer.
- `ClassifyAsync(IReadOnlyList<string> words, ct)`: primary API. Returns a never-null array parallel to the input
  with one full `WordAnalysis` per word, always, so there is no separate detail query. The words are processed by the
  single flow above and the context words are encoded in one pass, joined by single spaces.
- `ClassifyAsync(string text, ct)`: splits the text into tiles (words, whitespace runs and single other
  characters) that tile the input exactly and analyzes the words as above.
- `ContextLemmas`, `GetCandidates(lemma)`: describe the table. `ContextLemmas` lists only the lemmas that have a
  trained classifier (the dictionary has too many lemmas to list). `GetCandidates` lists every possible answer of any
  lemma of the table, in table order (one for most dictionary words, several for a context lemma or a word with more
  than one part of speech), and an empty list for an unknown lemma; it honors `UseLexicon`, so it never disagrees with
  classification.
Texts longer than `MaxSequenceLength` use a window around each target instead of one pass.
Features are the mean of the target word pieces' hidden states minus the table mean, normalized
(`FeatureExtractor`, shared with the builder).

## Error Handling

- Null arguments (encoder, table, tokenizer, words, a word, text) raise `ArgumentNullException`; a null `options`
  is not an error and means the defaults.
- `MaxSequenceLength` outside 8 to 512, a negative or NaN `AbstainMarginScale` and a negative `IntraOpThreads` raise
  `ArgumentOutOfRangeException` (positive infinity is accepted for `AbstainMarginScale`, so every context word
  abstains). The constructor checks the first two through `ValidateOptions`; a NaN scale reports the parameter name
  `options` (the range checks report the expression of the checked option); `CreateAsync` runs the same validation and
  also checks `IntraOpThreads`, all before it downloads or verifies any model file. A scale of 0 never abstains.
- The constructor that takes a `vocabularyPath` reads the vocabulary through `WordPieceTokenizer.FromFile`:
  a null path raises `ArgumentNullException`, an empty path `ArgumentException`, a missing file
  `FileNotFoundException`, another read failure `IOException`, and a vocabulary without the
  `[UNK]`, `[CLS]` or `[SEP]` token raises `ArgumentException`.
- A table whose model digest or dimension differs from the encoder's raises
  `WordSenseModelMismatchException`.
- A user-supplied `ModelDirectory` is verified against the pinned digests before use; a missing,
  altered or incomplete file raises an exception from `WordSenseModelLocation.FromDirectory`.
  If creating the encoder or classifier fails after the encoder was opened, the encoder is disposed.
- After `Dispose`, a classifier that owns its encoder throws `ObjectDisposedException` from the
  encoder on the next classification that needs it; a classifier built over a borrowed encoder leaves it open. A call
  whose words need no encoder does not touch it.
- A cancelled token raises `OperationCanceledException` before encoding starts and before each target is scored.
- Unknown words, dictionary words with several senses and abstained words are not errors; they are reported in the
  analysis (`Basis`, `Abstained`, `Candidates`).

## Interactions

Uses `WordPieceTokenizer`, `WordSenseTable` (the resolver and the entries), `IWordSenseEncoder`,
`FeatureExtractor` (pooling, centering and normalization of the target features) and `WordSenseModelDownloader`, and
the SenseRecord unit (the answer types, `TextSplitter` and `AbstainRule`). The
classifier is thread-safe when its encoder is: the table and tokenizer are immutable and the session is
shared.
