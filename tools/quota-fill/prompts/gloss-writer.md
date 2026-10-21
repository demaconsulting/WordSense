# Task: write real one-line glosses for senses that only have a placeholder gloss

You are a lexicographer. Each item is one sense of an English word in a project word list. Its current gloss is a
placeholder (for example `noun`, `verb`, `adjective-noun`, or the template "The preposition "about"; a project sense,
because ..."). Write a real, accurate, one-line gloss for the sense.

## Input

A JSON file (a list of items). Each item has:

- `id`: the sense id (`lemma#label`); copy it exactly to the output
- `lemma`, `pos` (part of speech name) and `role` (for example `preposition (closed class)`, `interjection`, `noun`)
- `label`: the readable label of the sense; for a derived or prefixed word (`rebid#second-bid`,
  `overdose#excessive-dose`) it hints at the meaning
- `current`: the placeholder gloss (ignore its wording)
- `forms`: inflected forms the sense covers, when known
- `other_senses_of_lemma`: up to six other senses of the lemma (`id (pos): gloss`); the new gloss must fit this sense
  and be distinguishable from them
- `examples`: 0-2 example sentences using the sense (`{...}` marks the word); when empty, rely on your knowledge of
  English

## Output

Write exactly one line per input item to the output file, tab separated: `id<TAB>gloss`. No header, no blank lines, no
quotes, no commentary. Keep the input order and do not skip any item.

## Style of a gloss

- Lower case start, no final period, 8 to 160 characters, a single line, no tab characters.
- Dictionary style as in WordNet: a noun is glossed as a noun phrase ("a second or repeated bid"), a verb with a verb
  phrase without the leading "to" ("bid again"), an adjective with an adjective phrase, an adverb with an adverb phrase.
- Closed-class words (preposition, conjunction, determiner, pronoun, auxiliary) and interjections: say what the word does
  or means, for example `on the rear side of or toward the stern of; behind (nautical)`, `introduces a clause of
  contrast: despite the fact that`, `used to express sudden pain or surprise`, `the definite article, marking a specific
  or already identified person or thing`, `modal verb expressing obligation or necessity`. Add a short parenthetical
  example only when it clarifies a function word, for example `(Pardon? I did not hear you)`.
- Do not repeat the lemma as the whole definition and never use a placeholder (`noun`, `verb`,
  `no definition recorded`).
- For archaic, dialect or rare words, say so briefly at the end in parentheses: `(archaic)`, `(Scots)`, `(nautical)`,
  `(computing)`.
- Be accurate: if you do not know a word, produce the most probable meaning from the lemma, the label, the other senses
  and the part of speech; never invent an etymology.
- Use US English spelling.

Read the input file, write the output file with the file-creation tool, and reply with only the number of lines written.
