### MiniLMWordSenseModel Unit Design

![MiniLM Structure](MiniLMView.svg)

#### Purpose

`MiniLMWordSenseModel` is the public model based on all-MiniLM-L6-v2, quantized to int8: the smaller and faster of
the two encoder models. It is the composition of its encoder model with the Basic model.

#### Data Model

A sealed public class that implements `IDownloadableWordSenseModel` and holds one `FallbackWordSenseModel`, built in
the constructor from two models it creates and
owns: an internal `MiniLMEncoderModel` (the primary model, a sealed class deriving from `OnnxWordSenseModel`) and a
`BasicWordSenseModel` (the fallback model). The two are also kept in the internal properties `Encoder` and `Fallback`
for the tests. The encoder model's description: model id
`all-MiniLM-L6-v2-int8`; files `model_int8.onnx` (about 21.9 MiB; 22 MiB with `vocab.txt`) and `vocab.txt`, each with a
pinned SHA-256;
lower-casing (uncased) tokenizer; hidden size 384; embedded weights `weights.bin` (fine-sense classifiers, form
choosers and per-lemma fine and group thresholds calibrated for this encoder) trained at sequence length 128; embedded
part-of-speech head `head.bin` (the format `WSH1`, bound to the pinned ONNX file; loaded and checked with the weights
and not used for answers).

#### Key Methods

The constructor `(string? modelDirectory = null, int maxSequenceLength = 128)` creates the encoder model with the
description (the base class validates the sequence length, 8 to 512) and the Basic model, and wraps both. `ModelId` and
`HiddenSize` come from the encoder model. An internal constructor `(string? modelDirectory, int maxSequenceLength, double
primaryWeight)` does the same with an explicit primary weight (the public constructor passes `PartOfSpeechPrimaryWeight`;
the builder tunes the weight and the tests pin it). `EnsureModelAsync(DownloadOptions? options = null, CancellationToken)`,
`ClassifyAsync` and `Dispose` delegate to the composite:
ensuring is optional (`ClassifyAsync` ensures lazily) and verifies or downloads the encoder files (the Basic model
needs none and is skipped by the composite), classification answers each word with the result of
the encoder model when it scored senses and otherwise with the complete result of the Basic model, and disposal
disposes both models once. When the encoder scored senses, the composite also re-splits the variants of a slot that the
encoder cannot tell apart: the encoder gives the analyses of one lemma and part of speech (the past and the infinitive
of `put`, the past and the present of `read`) the same probability, because they share their senses, so the composite
gives those tied variants the relative proportions that the Basic model gives them from the context (its shared tense
model), with the probability of the slot, the level, the confidence, the group, the sense and the senses of the encoder
result unchanged, and the selected analysis is the best variant of the first slot (see the `FallbackWordSenseModel`
unit document). When the encoder committed to no sense and no group (its result is at the part of speech level), the
composite also combines the slot probabilities of the encoder and of the Basic model with the primary weight 0.6
(`MiniLMWordSenseModel.PartOfSpeechPrimaryWeight`, passed to the `FallbackWordSenseModel`): the probability that the
encoder gives a slot is the mass of its senses and not a part-of-speech estimate, so a wrong top slot (the verb `use`,
which the encoder called a noun in 42 of 157 treebank tokens, 8 after the combination, measured on the branch of the
change) is repaired by the tagger of
Basic where the encoder is unsure. The weight was chosen on development data only (the open corpus development split
and the English EWT development file, `evaluate-running --split dev` with the matching gold file; the curve below was
measured before the clause state of the tagger was added and the weight was not chosen again): the accuracy on all
scored tokens (open dev / EWT dev) is 93.88% / 91.92% for the weight 1 (no combination), 93.97% / 92.00% for 0.95,
94.01% / 92.04% for 0.9, 94.07% / 92.11% for 0.8, **94.13% / 92.18% for 0.6**, 94.15% / 92.13% for 0.5, 94.07% / 91.97%
for 0.2 and 93.95% / 91.80% for 0 (the part of speech of Basic wherever the encoder names no sense), so the curve is flat
between 0.4 and 0.7 and its best sum is at 0.6. On the test files of the final build the weight 0.6 gains 0.24 points on
the open corpus and 0.27 points on the treebank (93.81% to 94.05% and 91.88% to 92.15% against the weight 1). The price
on the branch of the change was a deliberate trade under the rule 3 of the
reviewed suite: the row `M-change-4` (`The leaves change color in the fall`) went from fine to miss for MiniLM at every
weight of 0.9 or less, because Basic read `leaves change` as a noun compound (noun 0.814) and the blend followed the
confident tagger over the nearly even encoder (verb 0.534, noun 0.466); the clause state of the tagger repairs the row
(Basic now reads the verb), so no row is below the main branch in the final build, while five other rows go up (see
`corpus/suite/README.md`). An answer at the sense or group level, the real-text sense share and precision and the
share and precision of the SemCor sense evaluation do not change; only the part-of-speech-level answers, their
confidence and the fine top-1 of the selected analysis move. A caller that holds a `MiniLMWordSenseModel` calls
`await model.EnsureModelAsync();`
directly; a caller that holds only an `IWordSenseModel` tests for `IDownloadableWordSenseModel` first.

#### Error Handling

Inherited from the composite and the encoder model: the argument errors of `ClassifyAsync`, `ObjectDisposedException`
after `Dispose`, and `ArgumentOutOfRangeException` for a
sequence length outside 8 to 512 from the constructor. The typed errors of the encoder reach the caller unchanged from
`EnsureModelAsync` and from the lazy ensure and the loading of `ClassifyAsync`: `ModelUnavailableException` (files
missing or not downloadable), `ModelIntegrityException` (a supplied file, the embedded weights or the ONNX file is
damaged) and `DownloadConfigurationException` (invalid download options or `WORDSENSE_MIRROR_*` settings; it is not an
`ArgumentException`); the original exception is the inner exception. A damaged Basic table reports a
`ModelIntegrityException` from `ClassifyAsync`.

#### Dependencies

`FallbackWordSenseModel` (core), `OnnxWordSenseModel` (Onnx), `BasicWordSenseModel` (core) and the core contracts.

#### Callers

Application code and the builder (`ModelCatalog`, `weights minilm`, `evaluate minilm`).
