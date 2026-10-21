# Sentence labeler instructions (used by every labeling batch of the quota filling)

You label the meaning of one word in each of many sentences, using a fixed list of senses.

## Input

A JSON file `{"batch": ..., "groups": [...]}`. Each group has:

- `senses`: the list of possible senses, each with `id`, `pos` (part of speech) and `gloss` (a one-line definition);
- `items`: sentences, each with a number `n` and a `sentence` in which the target word is wrapped in braces, for
  example `He sat on the {bank} of the river.`

Every item of a group is labeled with the senses of **its own group only**.

## What to do

For each item decide which sense of its group the braced word has in that sentence, reading the whole sentence. Think
about the meaning and the grammar (noun, verb, adjective, ...) of the braced word. Do not choose a sense just because its
gloss shares a word with the sentence. The senses of one lemma can be close; pick the one whose gloss describes the use
best.

Answer `unsure` when:

- no listed sense fits (for example the braced word is part of a name or a title, or belongs to another word);
- the sentence does not settle the meaning, or two senses fit equally well;
- the sentence is garbled or not English.

Do not guess. A wrong label is worse than `unsure`.

For each sense answer, also give a confidence: `high` when the sentence clearly shows that sense and you would bet on it,
otherwise `low`.

## Output

Write a UTF-8 text file with one line per item, in the order of the input, tab-separated, no header, no blank lines:

```text
<n> <sense id> <high|low>
<n> unsure
```

The sense id must be copied exactly from the `senses` of the item's group. Every item of the input must have exactly
one line. Do not add comments or any other text to the file. Write the file with the file-creating tool (never with a
shell redirection), then reply with only: `done <number of lines>`.
