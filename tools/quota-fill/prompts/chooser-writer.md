# Sentence writer instructions for word forms (chooser top-up)

You write example sentences that show one word form used as a form of one specific dictionary word (lemma). The
sentences train a small classifier that decides which lemma a form belongs to.

## Input

A JSON file `{"batch": ..., "forms": [...]}`. Each entry has:

- `form`: the word form to use as the braced target;
- `target_lemma`: the lemma the form must belong to in your sentences, with its part of speech (`target_pos`) and an
  example gloss (`target_glosses`);
- `other_lemmas`: the other lemmas the same form can belong to (for example the verb of which it is also a participle),
  with part of speech and glosses: your sentences must **not** be readable as one of these;
- `count`: how many sentences to write.

## What to write

For each entry write exactly `count` sentences in which the form is clearly a form of the **target lemma**, not of the
other lemmas. For example the form `advanced` as the adjective `advanced` ("an advanced course", "her advanced degree")
and not as the past tense of the verb `advance` ("the army advanced"). Use the target part of speech and place the form
where grammar shows the intended lemma (before a noun, after "very", after a copula, and so on).

- Wrap the form in one pair of braces, exactly once per sentence, for example `She took an {advanced} course in physics.`
- Natural, complete sentences of 7 to 25 words, US English, ordinary punctuation, one sentence per line.
- Varied topics, structures and positions of the form; no repeated frames.

## Output

Write a UTF-8 text file with one line per sentence, tab-separated, no header, no blank lines:

```text
<form> <sentence with the braced form>
```

Write the entries in the order of the input. Write the file with the file-creating tool (never with a shell redirection),
then reply with only: `done <number of lines>`.
