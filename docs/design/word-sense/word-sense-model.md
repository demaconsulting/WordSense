# WordSenseModel Unit Design

## Overview

`IWordSenseModel` is the abstraction between the classifier and whatever produces word embeddings. The classifier
knows only this interface; tokenizing, windowing, pooling and the inference engine are the business of an
implementation, such as `MiniLMWordSenseModel` in the MiniLM package. The unit also holds
`WordSenseModelMismatchException`, which reports that a set of weights does not belong to a model or to a lexicon. The
unit contains `IWordSenseModel.cs` and `WordSenseModelMismatchException.cs`.

## Data Model

`IWordSenseModel` extends `IDisposable` and has these members:

- `ModelId`: the identifier of the model, for example `all-MiniLM-L6-v2-int8`; it names the model's files and weights.
- `ModelSha256`: the SHA-256 of the model file in lower-case hexadecimal.
- `HiddenSize`: the length of the vectors that `EncodeAsync` returns.
- `EncodeAsync(words, targets, cancellationToken)`: returns a task of an array with one entry per target, parallel to
  `targets`. An entry is the pooled contextual embedding of that target word, not centered and not normalized, or null
  when the model has no embedding for the word.
- `EnsureWeightsMatch(weights)`: a default-implemented method that throws `WordSenseModelMismatchException` when the
  model id (compared ordinally), the model SHA-256 (compared without regard to case) or the dimension of the
  `WordSenseWeightsTable` differs from the model's.

`WordSenseModelMismatchException` carries a message naming what differs (model, lexicon or sense count).

## Methods

- `EncodeAsync` takes the words of the text in order and the indexes of the target words. It must give the same
  vector for the same text and target every time, and it must be safe to call from several threads at once.
- `EnsureWeightsMatch` is called by the classifier constructor before it accepts a model and weights, so a weights
  table is never used with a model it was not trained for.

## Error Handling

- `EncodeAsync` throws `ArgumentNullException` for a null word list or target list, `ArgumentOutOfRangeException` when a
  target is not an index of the word list, `OperationCanceledException` when the token is cancelled and
  `ObjectDisposedException` after disposal.
- `EnsureWeightsMatch` throws `ArgumentNullException` for null weights and `WordSenseModelMismatchException` when the
  weights were trained for another model.
- A target without an embedding is not an error; the entry is null and the classifier reports the word as unknown.

## Interactions

Called by `WordSenseClassifier`, which owns the decision of which words become targets and which weights apply.
Implemented by `MiniLMWordSenseModel` in the MiniLM package and by a test double in the core tests. Uses
`WordSenseWeightsTable` (the identity it must match). The classifier is thread-safe exactly when its model is.
