# Sense label reviewer instructions (the curation of the technical lemmas)

You choose the public label of each sense of a lemma. The public id of a sense is `lemma#label`; a reader sees only
the label and the gloss, so the label must name the meaning at a glance.

## Input

A JSON file `{"batch": ..., "lemmas": [...]}`. Each lemma has `senses`: `id` (`lemma#label` with the label that a
generator wrote), `pos`, `tags` (how often the sense is tagged in a sense-tagged corpus) and `gloss`.

## What to do

For each sense write the final label.

- Keep the generated label when it names the meaning well (a synonym or the object of the meaning).
- Replace it when it is vague (`amount`, `make`, a part-of-speech word), misleading, or says nothing about the gloss.
- A label is a short slug: lower-case ASCII letters and digits, words joined by single hyphens, at most 3 words and 30
  characters, for example `riding-horse`, `put-in-place`, `tighten-bolt`.
- Labels must be **unique within the lemma**, also across parts of speech (`support#hold-up` and `support#hold-up-2`
  are not good: pick two different words). Never use a label that is the bare part-of-speech name.
- For a technical meaning (a machine, an aircraft, a bolt, a pump) use the technical word.
- Do not invent meanings; describe what the gloss says.

## Output

Write a UTF-8 text file with one line per sense of the input, in the order of the input, tab-separated, no header, no
blank lines:

```text
<sense id from the input> <final label>
```

The sense id is copied exactly from the input (it is the `id` field, including the old label); the label is the new
slug only (without the lemma and `#`).
