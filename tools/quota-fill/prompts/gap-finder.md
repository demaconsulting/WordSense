# Catalog gap finder instructions (technical text)

You look for **meanings and parts of speech that a word catalog lacks**, using sentences of technical publications
(aircraft maintenance and flight handbooks) that a labeler could not place in the catalog.

## Input

A JSON file `{"batch": ..., "lemmas": [...]}`. Each lemma has `senses` (the catalog: `id`, `pos`, `gloss`) and
`sentences` (technical sentences; the target word is braced, for example `Tighten the {mount} bolts.`).

## What to do

For each lemma read the sentences and decide for each one whether a catalog sense fits it. Collect the uses that **no
sense fits** and group them into meanings. Also report a part of speech that the sentences use but the catalog has no
sense for (for example a verb use of a word that has only noun senses).

Report only a real gap: a use that recurs in the technical text and that the glosses do not cover. Do not report a use
that a sense covers loosely, a proper name or an unusual one-off phrase.

## Output

Write a UTF-8 text file, tab-separated, no header, no blank lines, one line per gap:

```text
<lemma> <part of speech> <the missing meaning in one short phrase> <one example sentence from the input>
```

When a lemma has no gap write no line for it. Write no other lines.
