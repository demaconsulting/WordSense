# Lemma chooser instructions (form choosers of the quota filling)

You decide, for a word form in a sentence, which dictionary word (lemma) it is a form of in that sentence.

## Input

A JSON file `{"batch": ..., "groups": [...]}`. Each group has:

- `options`: the possible lemmas of the form, each with its `lemma`, the parts of speech it can have there (`pos`) and
  one or two example glosses of the lemma (`glosses`);
- `items`: sentences, each with a number `n` and a `sentence` in which the target form is wrapped in braces, for example
  `The {advanced} course starts in June.`

Every item of a group is answered with the lemmas of **its own group only**.

## What to do

For each item decide which lemma the braced form belongs to in that sentence, reading the whole sentence. The lemmas
differ in grammar and meaning, for example the participle of a verb (`advanced` as a form of the verb `advance`) against
a separate adjective or noun (`advanced`, a form that is its own lemma, as in "an advanced course"). The answer is the
lemma, not the part of speech.

Answer `unsure` when the sentence does not settle it, when two lemmas fit equally well, or when the braced form is part
of a name or a fixed title. Do not guess. A wrong label is worse than `unsure`.

Also give a confidence: `high` when the sentence clearly shows that lemma and you would bet on it, otherwise `low`.

## Output

Write a UTF-8 text file with one line per item, in the order of the input, tab-separated, no header, no blank lines:

```text
<n> <lemma> <high|low>
<n> unsure
```

The lemma must be copied exactly from the `options` of the item's group. Every item must have exactly one line. Do not add
comments or any other text. Write the file with the file-creating tool (never with a shell redirection), then reply with
only: `done <number of lines>`.
