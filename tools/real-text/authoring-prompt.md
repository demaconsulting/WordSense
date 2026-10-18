# Authoring Prompt (frozen)

> **Historical.** This page records an earlier phase of the project. The builder commands it names (for example
> `real-import`, `real-judge-*`, `validate-provenance`, `nounverb-*`, `learning-curve`, `error-diagnosis`,
> `weak-lemmas`) were removed from `WordSense.Builder`; the results stay as the record of how the data was built.

Two independent authors write the sentences that complete senses real text cannot fill: the **Anchor author** (training
sentences) and the **Holdout author** (evaluation sentences). Use a fresh agent for each role, with no shared context.
Neither author sees the other's menu or output. Each author gets only its own menu file and the rules below. The files
they write are imported with `real-authored-import --anchors FILE --holdout FILE`.

This text is frozen. Do not edit it between packages; change it only with a new version note at the end.

## Menu File (one per author)

One line per sense, tab-separated: `lemma`, `sense`, `part of speech`, `definition and pronunciation cue`,
`forms list` (the only allowed forms of the target word), `number of sentences to write`. The anchor menu asks for the
anchor deficit plus 2 spare sentences; the holdout menu asks for the holdout deficit plus 2 spare sentences. A menu never
mentions the other author's menu or counts.

## Rules Given to Both Authors

You write example sentences for a word sense disambiguation corpus. For every menu line write exactly the requested
number of sentences, each for the given sense of the lemma.

1. Write natural, varied sentences of 8 to 35 words. Vary the topic, register (casual, formal, technical, narrative),
   sentence shape (statements, questions, commands, subordinate clauses) and sentence opening.
2. Mark the target word with one pair of braces, for example `She drew the {bow} slowly.`. Exactly one non-empty pair per
   sentence, around exactly one word.
3. The target word must be one of the forms in the forms list of the menu line, an inflected form where the list has
   one. Spread the sentences over the listed forms; do not use the same form every time.
4. The sense must be unambiguous from the sentence alone: a reader who sees only the sentence must be able to tell which
   sense of the menu is meant, and the sentence must not fit another sense of the lemma on the menu.
5. Write every sentence yourself. Do not copy or closely paraphrase dictionary examples, corpus text, books, websites or
   famous quotations. Avoid named real people where you can.
6. Do not repeat a sentence, and do not give two sentences the same frame with only a word changed.
7. Do not mention the corpus, the menu, the sense label or the task in a sentence.
8. US English spelling.

## Output Format

Output only TSV, one sentence per line, three tab-separated columns and no header, no comments and no blank lines:

```text
lemma<TAB>sense<TAB>sentence with the {target} in braces
```

Use the lemma and the sense label exactly as in the menu.

## Extra Rule for the Holdout Author

Prefer everyday, non-technical wording and common sentence patterns, so that the holdout is not tuned to the vocabulary
the anchor author would choose.

## Independence

- The Anchor author and the Holdout author run in separate agents with separate menus.
- Neither author is shown the other's output, menu or counts, and neither is shown the existing corpus rows of the
  lemma.
- The orchestrator forwards only the author's own TSV file to the importer; overlap between the two files is rejected by
  the importer, so never fix an overlap by showing one author the other's sentences, but ask the author for a
  replacement sentence.

## Version

Version 1: initial text for Phase 4.
