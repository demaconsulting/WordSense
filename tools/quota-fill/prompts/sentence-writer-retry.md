# Sentence writer instructions, second round (senses that stayed short)

You write example sentences for specific word senses. These senses were tried before: a first writer produced sentences
that a blind labeler read as a **different** sense of the same word (a sibling sense), so most sentences were lost. Your
job is to write sentences that cannot be confused with the siblings.

## Input

A JSON file `{"batch": ..., "senses": [...]}`. Each entry has:

- `sense`: the sense id (copy it exactly into your output);
- `lemma`, `pos` (part of speech) and `gloss` (a one-line definition of the intended sense);
- `examples`: example uses of the sense from the dictionary, if any;
- `forms`: the allowed forms of the word. The braced target must be exactly one of these forms (lower case, or
  capitalized at the start of a sentence);
- `others`: the other senses of the same lemma (`id`, `pos`, `gloss`): the **siblings** to keep apart;
- `count`: how many sentences to write for this sense.

## What to write

For each entry write exactly `count` sentences in which the word is used **in the intended sense and part of speech**.

1. Read the `gloss` and the `others` first. Find the one feature that separates the intended sense from the nearest
   sibling (the kind of object, the kind of subject, a typical preposition, a typical collocate, a typical domain).
2. Build every sentence around that feature, with at least two **context words that only fit the intended sense** (a
   typical object, a typical subject, a domain word, a clear consequence). Put the cue next to the target word when you
   can.
3. Reject a sentence that a careful reader could also read in a sibling sense, and write another.
4. Vary the cue words, the topics (everyday talk, narrative, news, science, business, technical text), the sentence
   structures, the position of the target and the allowed forms. No two sentences may share a frame.

Wrap the target word in one pair of braces, for example `The {bank} raised its interest rates.`, exactly once per
sentence. Natural, complete sentences of 7 to 28 words, US English, ordinary punctuation, one sentence per line. Do not
quote or paraphrase the gloss. No lists, definitions, headings or proper names as the target.

## Output

Write a UTF-8 text file with one line per sentence, tab-separated, no header, no blank lines:

```text
<sense id> <sentence with the braced target>
```
