### OnnxClassifier Unit Design

![Onnx Structure](OnnxView.svg)

#### Purpose

`OnnxClassifier` is the internal helper of the encoder models: it resolves words from the lexicon, decides the fine
sense, the group or the part of speech of ambiguous words from the encoder's view of their context, and answers the
words without usable weights through the fallback model.

#### Data Model

An internal sealed class holding the shared encoder, the weights table (`WeightedLemmaTable`) and the fallback model
(an `IWordSenseModel`; in production the Basic model owned by `OnnxWordSenseModel`, and a test double in tests). It
holds no mutable state. Per call it builds one result per word and three work lists: words answered at once, words
pending an encoder decision and words that need the fallback.

A result is `WordSense(Word, Details, Level, GroupId, SenseId, Confidence)`. Per level:

| Level | Details | GroupId | SenseId | Confidence |
| --- | --- | --- | --- | --- |
| `None` | null | null | null | 0 |
| `PartOfSpeech` | the chosen analysis | null | null | see the cases below |
| `Group` | analysis of the group's lemma and part of speech | set | null | margin of the group level |
| `Sense` | analysis of the sense's lemma and part of speech | set | set | margin of the fine level, or 1 |

An analysis is chosen by lemma and part of speech; when several analyses of the form share them and differ only in
features, the first in lexicon order is used unless the form has a chooser (see _Form chooser_).

#### Key Methods

`ClassifyAsync(words, cancellationToken)`:

1. **Resolve.** Every word is looked up in lower case. An empty word or no analysis gives
   `(word, null, None, null, null, 0)`. The candidates of the word are the fine senses (`SensesOf`) of the lemma and
   part of speech of every analysis of the word, in lexicon order; no sense of another lemma is ever a candidate.
2. **Single sense.** A word with one analysis whose lemma and part of speech have exactly one sense gives that sense
   at the `Sense` level with confidence 1 and no encoder run, in every model.
3. **Weights.** For a word with several candidate senses the table is asked for the weights of the lemma of its
   analyses and, for a form with several lemmas or tenses, for the form's chooser. A candidate sense that has no
   column in the lemma's weights is dropped. When fewer than two candidates with a column remain, or the lemma has no
   weights, or a multi-lemma form has no chooser, the word goes to the fallback list (see _Fallback_). Otherwise the
   word is pending.
4. **Encode.** When at least one word is pending, the words are joined by single spaces into one text and the encoder
   runs once for the pending words. Each pooled vector goes through `FeatureExtractor.Center` (subtract the global
   mean, scale to unit length). A word without word pieces, or a vector whose dimension differs from the weights,
   sends the word to the fallback list.
5. **Form chooser.** For a pending form whose analyses belong to several lemmas (`left`, `bases`, `rows`, `found`) or
   to several tenses of one lemma (`read`, `reread`), the chooser of the form scores its analysis labels with the
   feature, and its margin `m` against its own threshold `t` is checked. If `m >= t`, the analysis of the best label is
   chosen and the candidates shrink to the senses of that analysis's lemma; the lemma's fine classifier then runs. If
   the margin is below the threshold the answer is the best label's analysis at the `PartOfSpeech` level with the
   confidence mapped from the chooser margin, which is below one half. A chosen lemma without weights is answered at
   the `PartOfSpeech` level in the same way.
6. **Levels.** `LemmaWeights` scores the candidates (one-versus-rest activation per fine sense), derives the group
   scores (the sum of the normalized fine probabilities per group) and decides the level:
   - the best fine sense is answered at the `Sense` level when its margin is at least `t_fine` of the lemma;
   - otherwise the best group is answered at the `Group` level when its margin is at least `t_group`;
   - otherwise the best group is answered at the `Group` level with the confidence below one half that the same
     mapping gives.

   A threshold that is "never" is not reached by any finite margin. A single candidate (one fine sense, or all
   candidates in one group) has an infinite margin and the confidence 1 at that level. The best candidate is returned
   even when its confidence is low.
7. **Confidence.** The confidence of the answered level is `clamp(0.5 + 0.5 * (m - t) / max(t, 0.5), 0, 1)` where `m` is
   the margin of the best against the second-best candidate of that level and `t` is the lemma's threshold for that
   level (`t_fine` or `t_group`; the chooser's threshold for the chooser). It is exactly 0.5 at the threshold, grows
   with the margin, is 0 for a non-numeric margin and is 1 for an infinite one.
8. **Fallback.** For the words on the fallback list the classifier asks the fallback model to classify the same word
   list once, so the model sees the same text. The answer of each such word is the Basic analysis (Details) at the
   `PartOfSpeech` level with no ids and with the Basic probability as confidence, whatever level Basic itself reports.
   The fallback is used for a word with several senses whose lemma has no trained weights, for a word with a single
   analysis and many senses and no weights, and for a word whose encoder vector is unusable. No fine sense is ever
   guessed. The fallback model needs no model file.

`NeedsModel`, `HasMultiSenseWord` and `ResolveWithoutModel` let the model answer text without ensuring or opening
anything: `NeedsModel` takes the weights table (the embedded weights, which need no model file) and is true only when a
word is pending, that is, has several candidate senses together with usable weights. Words that are unknown, single
sense or on the fallback list are answered without the encoder and without model files.

#### Error Handling

A null list throws `ArgumentNullException`; a null element throws `ArgumentException`; a canceled token throws
`OperationCanceledException`; an empty list gives an empty array and the result is parallel to the input. A vector
that cannot be used (no word pieces, or a dimension that differs from the weights) does not fail the call: the word is
answered by the fallback at the `PartOfSpeech` level. Encoder and fallback failures propagate.

#### Dependencies

`WordLexicon`, `OnnxEncoder`, `LemmaWeights` (weights table, feature computation, scoring, levels), the fallback
`IWordSenseModel` (the Basic model) and the core contracts.

#### Callers

`OnnxWordSenseModel.ClassifyAsync` and the builder (`Evaluator`).
