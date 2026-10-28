# Sense group proposer instructions (the near-duplicate groups of the stage 1 lemmas)

You find senses of one lemma and part of speech that a reader cannot tell apart, from their glosses.

## Input

A JSON file `{"batch": ..., "units": [...]}`. Each unit is one lemma and one part of speech with all its senses:
`lemma`, `pos`, `senses` (each with `id`, `tags` (how often the sense is tagged in a sense-tagged corpus, a usage hint
only) and the full `gloss`).

## What to do

For each unit propose **groups**: sets of two or more senses of that unit.

- `equivalent`: the senses are effectively one meaning. The source splits them finely, but a careful reader given a
  sentence could not say which of the glosses it uses, because the glosses are near-identical or one only restates the
  other with other words.
- `related`: the senses are a genuine family of close meanings (the same core idea applied to close objects or
  situations) that are still distinguishable, but easily mixed up.

Rules:

- A group has two or more senses of one unit and **never all senses of the unit** (the part of speech is the next level
  and says that already).
- A sense is in at most one group. Several groups per unit are fine.
- Judge by meaning, from the full glosses. Do not group senses only because they share a word, or because they have a
  common part of the gloss that is not the difference between them (`cut off a branch` and `cut off a speaker` differ).
- Do not group a literal sense with a figurative or technical one, or senses with a different subject or object type
  when that difference is the point of the gloss.
- Be conservative. Most units have no group, and a unit with few senses rarely has one. A wrong group costs more than a
  missed one.

## Output

Write a UTF-8 text file with one line per group, tab-separated, no header, no blank lines; write an empty file when there
is no group in the whole batch:

```text
<lemma> <pos> <equivalent|related> <slug> <member sense ids, comma-separated, copied exactly from the input> <reason>
```

`slug` is a name for the group: lower-case ASCII letters and digits, words joined by single hyphens, at most 3 words,
unique within the lemma (for example `pulling`, `social-dance`). `reason` is a short text without tabs.
