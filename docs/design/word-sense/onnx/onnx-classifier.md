### OnnxClassifier Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxClassifier` is the internal helper of the encoder models: it resolves words from the lexicon and
decides the senses of ambiguous words from the encoder's view of their context.

#### Data Model

An internal sealed class holding the shared encoder and the weights table. Per call it builds one result per word and
the list of words still pending a decision.

#### Key Methods

`ClassifyAsync(words, cancellationToken)`:

1. For each word, look up the lower-cased word in the lexicon: an empty word or no entry gives `(word, null, 0)`; one
   entry gives `(word, entry, 1.0)`.
2. Several entries: find the weights of the lemma. With weights the word is pending. Without weights (never trained,
   or dropped by a mismatch) the result is the first lexicon entry with confidence `1/n` for `n` entries, with no
   encoder run.
3. If at least one word is pending, run the encoder once over the text for all pending words. Each pooled vector goes
   through `FeatureExtractor.Center` (subtract the global mean, scale to unit length).
4. Score only the word's own candidate senses (a subset of the lemma's trained senses, in lexicon order; further
   lexicon senses that were not trained are never ranked). `Details` is the best
   candidate even when its confidence is low. A single candidate has an infinite margin.
5. The confidence is `clamp(0.5 + 0.5 * (m - a) / max(a, 0.5), 0, 1)` where `m` is the margin between the best and the
   second-best score and `a` is the abstain margin of the lemma. It is exactly 0.5 at the abstain margin, increases
   with the margin, and is 0 for a non-numeric margin. A word without word pieces gets `(word, null, 0)`.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`. A vector whose dimension differs from the weights gives `(word, null, 0)` for that word.
Encoder failures propagate.

#### Dependencies

`WordLexicon`, `OnnxEncoder`, `LemmaWeights` (weights table, feature computation and scoring) and the core contracts.

#### Callers

`OnnxWordSenseModel.ClassifyAsync` and the builder (`Evaluator`). `HasMultiSenseWord`, `NeedsModel` and
`ResolveWithoutModel` let the model answer text without ensuring or opening anything when no word has several senses
together with intact weights; `NeedsModel` takes the weights table (the embedded weights, which need no model file), so
a multi-sense word without weights (`so`, `will`) is answered from the lexicon alone.
