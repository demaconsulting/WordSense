# Sentence writer instructions (the top-up of the quota filling)

You write example sentences for specific word senses. The sentences train a word-sense classifier, so each one must show
the intended sense clearly and the set of sentences of a sense must be varied.

## Input

A JSON file `{"batch": ..., "senses": [...]}`. Each entry has:

- `sense`: the sense id (copy it exactly into your output);
- `lemma`, `pos` (part of speech) and `gloss` (a one-line definition of the intended sense);
- `examples`: example uses of the sense from the dictionary, if any;
- `forms`: the allowed forms of the word. The braced target must be exactly one of these forms (in lower case, or
  capitalized at the start of a sentence);
- `others`: the other senses of the same lemma (`id`, `pos`, `gloss`), so that you avoid sentences that could also fit
  them;
- `count`: how many sentences to write for this sense.

## What to write

For each entry write exactly `count` sentences in which the word is used **in the intended sense and part of speech**.

- Wrap the target word in one pair of braces, for example `The {bank} raised its interest rates.`, exactly once per
  sentence. Only the target form is braced.
- The context must make the intended sense clear to a reader who does not see the gloss, and must not fit a different
  sense in `others` as well. Do not quote or paraphrase the gloss.
- Natural, complete sentences of 7 to 28 words, US English, ordinary punctuation, one sentence per line.
- Be varied: different topics, registers (everyday talk, narrative, news, science, business, history), sentence
  structures and sentence positions of the target; use several of the allowed forms when they exist; do not start many
  sentences the same way; do not repeat a frame with only a few words changed.
- Real-world style, not textbook style. No lists, no definitions, no headings, no proper names as the target.

## Output

Write a UTF-8 text file with one line per sentence, tab-separated, no header, no blank lines:

```text
<sense id> <sentence with the braced target>
```

Write the entries in the order of the input, all lines of an entry together. Write the file with the file-creating tool
(never with a shell redirection), then reply with only: `done <number of lines>`.
