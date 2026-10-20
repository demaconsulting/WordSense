# Migration working files (temporary)

`legacy-class-map.tsv` maps each class id of the retired class inventory (the 1,601 classes of the former
`coarse_class` table, for example `lead#guide-noun`) to the new fine senses (`sense_id`, for example `lead#leadership`)
and groups (`group_id`, when the sense is in a group) that cover it. It exists only to relabel our own corpus (anchors,
holdout and real-text sentences) to the fine senses: a class with one sense relabels deterministically, a class with
several senses narrows the candidates that a judge chooses from, and the respelling and meaning of the old class are
kept for the lemmas that have them.

**The file is temporary and internal.** It is not part of the word data (`validate-data` does not read it), it is not
shipped, published or listed in the appendix, and nothing promises users a mapping from the old class ids. **The
relabeling of our own corpus is done** (see "Relabeled Legacy Corpus" in `../README.md`), but the file is not deleted
yet: the Basic model still trains from the legacy corpus, and its trainer (`AnalysisLabels`) rolls the legacy labels up
to analysis labels with this map. It is deleted together with `../legacy/` when the Basic model no longer needs the
legacy corpus.

**The file is now a committed record, not regenerated.** It was written once by the `legacy-class-map` builder command
from the class tables, after the group data had its final form (a group exists only when curated, so a sense that is in
no group has an empty `group_id`). The command, the `LegacyClassMap` class and the class tables themselves were then
retired, so the file cannot be written again; edit it by hand only to fix a mapping.

| Column | Meaning |
| --- | --- |
| `class_id`, `kind`, `lemma`, `pos` | the old class, its kind, its lemma and part of speech letter |
| `meaning`, `respelling` | the pronunciation meaning and respelling of the old class (empty when it had none) |
| `sense_count` | the number of new senses of the class (0 for a class that stands for an entry) |
| `sense_id`, `group_id` | one new sense of the class (`lemma#label`) and its group (empty if it is in no group) |
| `entry_id`, `entry_lemma`, `entry_pos`, `features` | the entry and features of an entry class |

A group id would be `lemma#group:slug`. Group ids are versioned per data release and are not frozen, so `group_id` is
empty in every row of this record and the relabeling uses the sense ids only.

The kinds are `sense_group`, `no_oewn`, `unmapped`, `lemma_split` and `feature_only`; the last two are entry classes.

**The `sense_id` column holds the labels that `label-senses` generated at the time of the map**, before the curation of
the second batch changed 4,077 of the 6,256 ids it names. `sense-id-map.tsv` (`old_sense_id`, `sense_id`) gives the
current id of each changed one; an id that is not in it did not change. The relabeling applied this translation.
