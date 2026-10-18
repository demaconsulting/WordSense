# POS Audit Judge Prompt

This is the frozen prompt that the blind judges (A, B) and the adjudicator (C) of the part-of-speech audit received.

## Task

You judge the true part of speech of one braced word in each of many English sentences. Input is a tab-separated file
with one row per line: `id`, `lemma`, `sentence`. The sentence has exactly one word in braces, for example
`The {record} was broken.` Judge the word inside the braces, as it is used in that sentence. The `lemma` column is the
dictionary headword the braced word belongs to. Never open any other file, never run tools other than reading the
input file and creating the output file, and never look at the corpus directory.

## Output

Create one tab-separated output file with exactly one line per input row, same ids, no header and no extra lines:
`id TAB pos TAB form TAB conf TAB note`

- `pos`: `noun`, `verb`, `adj`, `adv` or `other` (preposition, pronoun, determiner, interjection, proper noun,
  conjunction, or a word that is not a usage of the lemma). `other` needs a note.
- `form`: `base`, `third`, `past`, `pastpart`, `presentpart`, `infinitive`, `comparative`, `superlative`, `positive`,
  `singular`, `plural` or `none`. Use the verb forms for verbs (`past` for a simple past, `pastpart` for a past
  participle, `presentpart` for -ing, `infinitive` for the bare form after "to" or a modal, `base` for other present
  forms), `singular`/`plural` for nouns, `comparative`/`superlative`/`positive` for adjectives and adverbs.
- `conf`: `H` (certain), `M` (probable), `L` (ambiguous). Use `L` whenever the usage is truly ambiguous, such as a
  participial adjective ("a {used} car"), a gerund versus a noun, or "the {closer} the better". `L` needs a note.
- `note`: a short free-text reason, required for `other` and `L`, otherwise empty. No tab characters.

Judge the usage in the sentence, not what is likely for the word in general. Participles that act as adjectives
("an {alleged} crime", "the {used} car") are `adj`; participles that form a verb phrase ("was {used} to cut") are
`verb` with `pastpart`. A noun used as a modifier of another noun stays `noun`. Return the file path when done and
nothing else.

## Adjudicator (C)

Judge C additionally receives, per row, the answers of A and B (as `pos/form/conf: note`). It writes the same output
format; the `note` is its reasoning. It must decide by the sentence alone.
