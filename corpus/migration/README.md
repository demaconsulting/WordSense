# Migration working files (temporary)

`legacy-class-map.tsv` maps each class id of the retired class inventory (the 1,601 classes of `data/coarse_class`,
for example `lead#guide-noun`) to the new fine senses (`sense_id`, for example `lead#leadership`) and groups
(`group_id`) that cover it. It exists only to relabel our own corpus (anchors, holdout and real-text sentences) to the
fine senses: a class with one sense relabels deterministically, a class with several senses narrows the candidates that
a judge chooses from, and the respelling and meaning of the old class are kept for the lemmas that have them.

**The file is temporary and internal.** It is not part of the word data (`validate-data` does not read it), it is not
shipped, published or listed in the appendix, and nothing promises users a mapping from the old class ids. It is deleted,
together with the `legacy-class-map` builder command and the `LegacyClassMap` class, when the relabeling is done.

The file is generated and deterministic: `dotnet run --project tools/WordSense.Builder -c Release -- legacy-class-map`
(run after `label-senses`) writes it again from `data/` and `corpus/senses`.

| Column | Meaning |
| --- | --- |
| `class_id`, `kind`, `lemma`, `pos` | the old class, its kind, its lemma and part of speech letter |
| `meaning`, `respelling` | the pronunciation meaning and respelling of the old class (empty when it had none) |
| `sense_count` | the number of new senses of the class (0 for a class that stands for an entry) |
| `sense_id`, `group_id` | one new sense of the class (`lemma#label`) and its group (empty for an entry class) |
| `entry_id`, `entry_lemma`, `entry_pos`, `features` | the entry and features of an entry class |

The kinds are `sense_group`, `no_oewn`, `unmapped`, `lemma_split` and `feature_only`; the last two are entry classes.
