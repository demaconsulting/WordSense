# Curated sense inventory (input of `seed-inventory`)

`senses.tsv` and `groups.tsv` are the reviewed curation of the first batch of lemmas (20 lemmas, 297 senses, 191
groups): `back`, `even`, `first`, `forward`, `go`, `help`, `home`, `however`, `just`, `little`, `long`, `only`, `own`,
`please`, `pretty`, `review`, `right`, `still`, `test` and `well`. The ids are public ids
(`lemma#label`). The columns `tier`, `tag_count` and the others are the per-sense data of the curation (the tier sizes
the training data of the sense); `seed-inventory` reads `sense_id`, `oewn_sense_id`, `part_of_speech`, `gloss`, `source`
and `group_id` of `senses.tsv` and `group_id`, `lemma`, `part_of_speech` and `member_sense_ids` of `groups.tsv`.

`dotnet run --project tools/WordSense.Builder -c Release -- seed-inventory` writes them into `data/` as the tables
`sense_label` (origin `curated`, or `project` for the four senses that Open English WordNet does not have:
`right#discourse-marker`, `well#discourse-marker`, `help#cry-for-aid` and `however#conjunction`) and `sense_group`
and `group_sense` (origin `curated`). A curated group that mixed two parts of speech is split by the importer, and a
group whose id was also the id of a sense gets the suffix `-group`. After the import the tables are edited in `data/`
and these files are only the record of what was imported.
