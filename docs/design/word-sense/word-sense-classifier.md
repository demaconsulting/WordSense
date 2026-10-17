# WordSenseClassifier Unit Design

## Overview

`WordSenseClassifier` (interface `IWordSenseClassifier`) is the public entry point. It owns or
borrows a model, and holds a lexicon table and the weights trained for the model; it answers "which part of speech,
and for a heteronym which pronunciation, does this word have here". It works with or without a model: the static
`Create()` builds a lexicon-only classifier from the embedded lexicon, which needs no model file and no ONNX Runtime.

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
  and runner-up are still reported. A context word for which the model returns no vector, or whose feature vector has
  zero norm, cannot be scored and yields the unknown result (basis `None`, null `Details`, `Abstained` false, zero
  margins, no candidates).
- `WordSenseToken(Start, Length, Text, Analysis)`: a tile of the input text of the raw-string overload; the type is
  declared in `WordSenseClassifier.cs` next to `IWordSenseClassifier` and is owned by this unit. `Analysis` is the
  analysis of a word tile and is the unknown-word analysis for any other tile. Invariant: the tiles returned for a
  text are in order with no gaps or overlaps, so concatenating every `Text` reproduces the input; they come from
  `TextSplitter.SplitSegments`.
- `IWordSenseClassifier`: the public interface, declared in `WordSenseClassifier.cs`, implemented by
  `WordSenseClassifier`. It exposes `ContextLemmas` (the lemmas that have a trained classifier, sorted) and
  `GetCandidates(lemma)`.
- `WordSenseClassifierOptions`: `AbstainMarginScale` (default 1.0) and `UseLexicon` (default true). The options that
  concern a model (model directory, download, sequence length and threads) belong to the model package.

## The Single Flow

Every word, whatever kind, takes the same path. There is one resolver and one decision rule:

1. **Resolve**: `table.TryResolve(word, UseLexicon, out resolution)` maps the surface word to a lemma entry (form
   map, exact lemma, possessive of a context lemma, gated morphology; see the table unit). No other lookup exists.
2. **Case policy** (only for a dictionary entry, which is not contextual): a dictionary entry is used when the word is
   lower case, or is the first word of the input, or directly follows a token that is exactly `.`, `!`, `?` or `:` or
   an opening quote or bracket, or is exactly `I`; otherwise the word is treated as unknown. A capitalized word
   elsewhere is usually a name, and measurement showed that treating those as dictionary words was wrong far too
   often. Contextual entries are not subject to the policy, so the behavior of context lemmas is unchanged. For
   `ClassifyAsync(words)` the previous token is `words[i-1]`; for the text overload it is the previous non-space tile.
3. **Decide** from the number of senses `n` (1 when morphology narrowed the entry, otherwise the number of senses of
   the entry) and whether the classifier has a model and weights:

   | Entry | Action | Basis |
   | --- | --- | --- |
   | Not found, or rejected by the case policy | unknown, no candidates | `None` |
   | `n == 1` | answer that sense directly, no model | `Dictionary` |
   | `n > 1`, contextual, with model and weights | embed and score; answer, or abstain below the threshold | `Context` |
   | `n > 1`, no model or not contextual | no answer, `Abstained` set, candidates are the senses | `Dictionary` |

   A lexicon word is just an entry with one sense. There is no special-case code for dictionary words except the
   case policy.
4. **Embed only when needed**: the classifier first resolves every word, then calls the model only if the classifier
   has a model and at least one word resolved to a context lemma. Dictionary answers, candidate-only words and unknown
   words never reach the model, so a sentence made only of such words costs no inference, and a lexicon-only
   classifier never embeds. When the model runs, it receives the words and the indexes of the context targets, and
   only the context words are scored; dictionary words in the same call are answered from the table.
5. **Score**: the vector of a target is centered with the weights table mean and normalized (`FeatureExtractor`,
   shared with the builder); the lemma's `SenseWeights` score the senses.

`UseLexicon = false` makes dictionary entries invisible to the resolver (no dictionary lookup, no morphology), so
the classifier answers exactly the context lemmas and their forms; words derived by morphology are therefore unknown
and `GetCandidates` of a dictionary-only lemma is empty. The answer `Basis` is `Context` when the lemma's classifier
decided, `Dictionary` when the table alone decided and `None` for an unknown word.

## Methods

- `WordSenseClassifier.Create(options)`: builds a lexicon-only classifier from the embedded lexicon, with no model and
  no weights. Single-sense words are answered; a context lemma is a candidates-only word (basis `Dictionary`,
  `Abstained` set).
- Constructor `(table, model, weights, options, ownsModel)`: the model and the weights are given together or not at
  all. With a model it calls `model.EnsureWeightsMatch(weights)` and `weights.EnsureMatches(table)`, so the model, the
  weights and the lexicon are known to belong together. `ownsModel` says whether `Dispose` disposes the model; it has
  no effect without a model.
- `ClassifyAsync(IReadOnlyList<string> words, ct)`: primary API. Returns a never-null array parallel to the input
  with one full `WordAnalysis` per word, always, so there is no separate detail query. The words are processed by the
  single flow above and the context words are embedded in one model call.
- `ClassifyAsync(string text, ct)`: splits the text into tiles (words, whitespace runs and single other
  characters) that tile the input exactly and analyzes the words as above.
- `ContextLemmas`, `GetCandidates(lemma)`: describe the table. `ContextLemmas` lists only the lemmas that have a
  trained classifier (the dictionary has too many lemmas to list). `GetCandidates` lists every possible answer of any
  lemma of the table, in table order (one for most dictionary words, several for a context lemma or a word with more
  than one part of speech), and an empty list for an unknown lemma; it honors `UseLexicon`, so it never disagrees with
  classification.
- `Dispose`: disposes the model when the classifier owns it; a lexicon-only classifier has nothing to dispose.

Tokenizing, windowing of long texts and pooling of the word pieces of a target are done by the model, not by the
classifier.

## Error Handling

- Null arguments (table, words, a word, text) raise `ArgumentNullException`; a null `options` is not an error and
  means the defaults.
- A model without weights, or weights without a model, raise `ArgumentException`.
- A negative or NaN `AbstainMarginScale` raises `ArgumentOutOfRangeException` (positive infinity is accepted, so every
  context word abstains). The constructor and `Create` run the validation. A scale of 0 never abstains.
- Weights that do not belong to the model, or to the lexicon (a different lexicon digest, other context lemmas or a
  different sense count), raise `WordSenseModelMismatchException`.
- After `Dispose`, a classifier that owns its model throws `ObjectDisposedException` from the model on the next
  classification that needs it; a classifier built over a borrowed model leaves it open. A call whose words need no
  model does not touch it, so a lexicon-only classifier keeps working after `Dispose`.
- A cancelled token raises `OperationCanceledException` before the model is called and before each target is scored.
- Unknown words, dictionary words with several senses and abstained words are not errors; they are reported in the
  analysis (`Basis`, `Abstained`, `Candidates`).

## Interactions

Uses `WordSenseTable` (the resolver and the entries), `WordSenseWeightsTable` (the weights and the mean),
`IWordSenseModel` of the WordSenseModel unit (the embeddings and the match check), `FeatureExtractor` (centering and
normalization of the target features), and the SenseRecord unit (the answer types, `TextSplitter` and
`AbstainRule`). It does not use a tokenizer or a downloader. The `MiniLMWordSense` factory of the MiniLM package
builds a classifier with a model it owns. The classifier is thread-safe when its model is: the tables are immutable.
