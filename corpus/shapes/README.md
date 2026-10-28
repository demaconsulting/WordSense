# The shape probe set

A held-out evaluation set of 560 sentences in which a word that is both a noun and a verb has one of seven common sentence
shapes. It measures how well a model tells a noun use from a verb use of the same spelling where the sentence is short,
technical or imperative (`Make a quick repair.`, `Close the valve.`).

It is evaluation data only: no training step reads it and no setting is chosen on it. The leakage check
(`WordSense.Builder check-leakage`) verifies that no sentence of it is in the anchors, the held-out sentences, the real-text
rows, the suite, the open corpus or an example sentence of Open English WordNet, and the quota-filling tools refuse to write
a sentence of it or a near copy.

## Provenance and license

The sentences are project-authored content dedicated to the public domain under CC0 1.0 by the project owner, like every
project-written row of the corpus. A language model (`gpt-5.6-luna`), a different model from the one that wrote the shape
anchors (`claude-sonnet-5.5`), wrote one sentence for a requested lemma, part of speech and shape, without seeing any anchor
or consumer sentence. Two independent labelers (`gpt-6.1-sol` and `claude-sonnet-5.5`) then labeled the part of speech of the
braced word without seeing the intended one. A sentence is kept only when both gave the same part of speech with high
confidence and it is the gold of the row. The set was drawn from the kept sentences with a fixed seed: 80 for each shape,
at most one for each lemma and shape, from lemmas of the base stage, of stage 1 and of stage 2 (the stages are those of
`Corpus.StagesOf`).

## File

`probe.tsv` has the columns `id`, `shape`, `lemma`, `gold`, `target_index` and `sentence`. The sentence is the words separated
by single spaces (punctuation as separate words) with the target word in braces, and `target_index` is the zero-based index of
the braced word. `gold` is `Noun` for a noun shape and `Verb` for a verb shape.

| Shape | Gold | The target word is |
| --- | --- | --- |
| `n-obj-light` | Noun | the object of a light verb (do, make, run, perform, take, give, have) after a determiner |
| `n-compound-head` | Noun | the head of a noun compound, after a modifier noun |
| `n-modifier` | Noun | a noun that modifies the noun after it; the lemma has no adjective sense in the lexicon |
| `n-subject-initial` | Noun | the first word of the sentence and its subject, with no article |
| `n-measure` | Noun | a noun in a measurement or quantity frame with a number |
| `v-imperative-initial` | Verb | the first word of an instruction, in the base form |
| `v-after-modal` | Verb | in the base form after a modal or the infinitive marker `to` |

## Evaluation

`WordSense.Builder evaluate-shapes [--model all] [--output FILE] [--outcomes FILE]` classifies each sentence on its own and
reports, for each shape and model, the share of rows whose selected analysis has the gold part of speech, and the harmful
rate: a gold noun answered as a verb with a confidence of at least 0.45. `--outcomes` writes the answer of every model for
every row.
