# Curated sense inventory (input of `seed-inventory`)

`senses.tsv` and `groups.tsv` are the reviewed curation of the first batch of lemmas (20 lemmas, 297 senses, 179 groups):
`back`, `even`, `first`, `forward`, `go`, `help`, `home`, `however`, `just`, `little`, `long`, `only`, `own`, `please`,
`pretty`, `review`, `right`, `still`, `test` and `well`. The ids of senses are public ids (`lemma#label`). The columns
`tier`, `tag_count` and the others of `senses.tsv` are the per-sense data of the curation (the tier sizes the training
data of the sense); `group_id` and `group_reason` of a sense record the curator's group of the sense (empty when the
sense is in no group any more, see "Group changes" below) and `flag` is the curator's remark (`D` near-duplicate of
another sense, `R` rare). `seed-inventory` reads `sense_id`, `oewn_sense_id`, `part_of_speech`, `gloss` and `source`
of `senses.tsv`, and `group_id`, `lemma`, `part_of_speech`, `member_sense_ids`, `kind` and `evidence` of `groups.tsv`.

`dotnet run --project tools/WordSense.Builder -c Release -- seed-inventory` writes them into `data/` as the tables
`sense_label` (origin `curated`, or `project` for the four senses that Open English WordNet does not have:
`right#discourse-marker`, `well#discourse-marker`, `help#cry-for-aid` and `however#conjunction`) and `sense_group`
and `group_sense` (origin `curated`). The run is idempotent: a second run changes nothing.

## Part of speech of a curated sense

The `part_of_speech` of a curated sense must be the part of speech of its sense in `data/` (the part of speech of its
Open English WordNet entry; an adjective satellite is an adjective); `seed-inventory` refuses the curation otherwise.
`right#agreement-yes` was curated as an interjection although Open English WordNet files the sense (`oewn-00151409-r`)
under the adverbs; it is an adverb here, as in the corpus rows. `back#animal-rear` is an adjective on purpose: it is the
adjective satellite `oewn-00132764-s` (`back%5:00:00:posterior:00`, "located at or near the back of an animal", as in
the back legs of a horse); the noun use of a horse's back is `back#body-trunk`.

## Groups

A **group exists only when it is needed**: a set of at least two, but not all, senses of one lemma and one part of
speech that are closely related in meaning and confusable. The curated name of a group in `groups.tsv` is `lemma#slug`;
`seed-inventory` writes it as the group id `lemma#group:slug` (for example `help#assistance` becomes
`help#group:assistance`). The marker `group:` keeps the id of a group apart from the ids of senses, so there is no
collision and no suffix. Group ids are versioned per data release and are not frozen (see `data/README.md`).

**Admission rule.** A group needs the curator's same-meaning judgment **and** evidence: observed confusion (the blind
judge or a classifier mixes the senses up) or near-identical glosses. `groups.tsv` records both:

- `kind` is `equivalent` (near-duplicate senses) or `related` (a genuine family of meanings). The curator's rule for the
  groups that have no judge data: `equivalent` when every non-head member (the member with the highest WordNet tag
  count is the head) is flagged `D` or the glosses are near-identical, otherwise `related`. For the groups that were
  named after one of their members, the kind follows the review of those groups (a family of meanings is `related`, a
  head with near-duplicates is `equivalent`).
- `evidence` is a short text that the importer requires to be non-empty for every group it writes: `judge: C/N in-group
  confusion, A in-group alternates` (the blind judge on N sentences of the group confused its senses C times and named
  another member of the group as the alternative A times when it was unsure), `curated:flag ...` (the `D` flags of the
  curation), or `curated:gloss ...` (the glosses are near-identical, or the meanings are related as the curator judged
  from the glosses).

The importer applies these rules to the curated groups, in this order:

- A curated group that mixes two parts of speech is split by part of speech (three of the groups).
- A group, or a part of a split group, with **one sense** is left out (122 of the parts): a sense needs no group.
- A group with **every sense of its lemma and part of speech** is left out (4 parts): the part of speech is the next
  level, so such a group says nothing more.
- A group that remains needs a `kind` of the `group_kind` vocabulary of `data/schema.yaml`, non-empty `evidence` and a
  slug of lower-case ASCII letters, digits and hyphens; otherwise the import stops and writes nothing.

The result is 56 groups of 138 senses: 26 `equivalent` and 30 `related`. The importer replaces the groups of the lemmas
of the curation as a whole and removes any group row whose origin is not `curated` (the retired default groups). After
the import the tables are edited in `data/` and these files are only the record of what was imported.

## Group changes

The cleanup pass of the groups (the data had 68 groups of 164 senses before it) made these changes. Group ids changed in
the data from `lemma#label` (28 of them `lemma#label-related`, the suffix that resolved a clash with the id of a sense)
to the scheme `lemma#group:slug`; they are not frozen, so this is recorded here and in the release notes of the release
that publishes the groups.

**Renamed (56 groups).** Every kept group got the marker, and the suffix `-related` was removed from the 16 kept groups
that had it (`help#assistance-related` is now `help#group:assistance`). Two groups also got a neutral slug, because the
old name was that of one member only:

| Old id | New id | Why |
| --- | --- | --- |
| `first#first-item-related` | `first#group:first-in-order` | both members are near-duplicates; neither names it |
| `still#distillery-related` | `still#group:distillation` | apparatus and plant of distillation; neither names it |

**Dropped (12 groups).** The curated groups of near-duplicate senses without evidence beyond the curator's judgment
(no observed confusion, or one in-group confusion), and the groups that are not one meaning. The member senses stay;
their `group_id` in `senses.tsv` is now empty.

| Dropped group | Why |
| --- | --- |
| `back#rear-located-related` | near-duplicates; no in-group confusion (0/48) |
| `forward#front-located-related` | near-duplicates; no in-group confusion (0/51) |
| `help#assist-related` | near-duplicates; 1 in-group confusion (1/48) |
| `just#exactly-related` | near-duplicates; no in-group confusion (0/48) |
| `long#spatial-length-related` | near-duplicates; no in-group confusion (0/40) |
| `please#give-pleasure-related` | near-duplicates; no in-group confusion (0/41) |
| `still#calm-water-related` | near-duplicates; 1 in-group confusion (1/27) |
| `well#favorably-related` | near-duplicates; no in-group confusion (0/27) |
| `right#right-side-related` | a turn, a hand and a location: not one meaning; no confusion (0/65) |
| `help#serve-food-related` | the two meanings are barely related; no confusion (0/27) |
| `well#advisable-related` | a weak link between the meanings; no confusion (0/34) |
| `home#on-target-related` | no evidence; no confusion (0/27) |

**Kept with a decision.** `test#group:trial` is `related` (18/65 in-group confusion); `only#group:merely` is `related`
and the `D` flag of `only#exclusively` was cleared, because it is a major adverb reading (226 WordNet tags) and not a
near-duplicate; `first#group:first-in-order` is `equivalent` (both members are `D`); `still#group:distillation` is
`related`.

**Known gaps of the evidence.** Six `related` groups that were named after a member have no observed in-group
confusion (`home#group:domestic`, `home#group:homeland`, `review#group:look-again`, `still#group:distillation`,
`test#group:examine-knowledge` and `test#group:try-out`), and two of them (`home#group:domestic` and
`test#group:try-out`) have no in-group alternate either; they stay on the curator's judgment of related meanings. The
groups without judge data (the other 40) rest on the curation flags and glosses only. These are candidates to drop in a
later release if a larger judge run still shows no confusion.
