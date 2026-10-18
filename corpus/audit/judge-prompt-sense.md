# Sense Audit Judge Prompt

This is the frozen prompt that the blind judges (A, B) and the adjudicator (C) of the real-sentence sense audit
received. The judges never see the source, the licence, the contributor or any proposed label.

## Task

You judge which dictionary sense a braced word has in each of many English sentences. The input is a tab-separated
file. Lines starting with `#menu` describe the sense menu of one lemma:
`#menu TAB lemma TAB sense TAB tags TAB definition TAB example // example // example`. All other lines are rows:
`id TAB lemma TAB sentence`. The sentence has exactly one word in braces, for example `He drew the {bow}.` Judge the
braced word as used in that sentence and pick the one menu sense of its lemma that fits. Use the part of speech in
the `tags` column as a hard constraint (a noun use never takes a verb sense, a nominal gerund such as "the upsetting
of the cart" is a noun, an adjectival participle is an adjective). A capitalised nationality or region modifier is an
adjective; a person or the language is a noun. If no menu sense fits (a different word, a proper name, a fixed
phrase that the menu does not cover, a typo), answer `none`. Never open any other file, never run tools other than
reading the input file and creating the output file, and never look at the corpus directory.

## Output

Create one tab-separated output file with exactly one line per row (not per menu line), same ids, no header and no
extra lines: `id TAB sense TAB conf TAB note`

- `sense`: a sense id from the lemma's menu, or `none`.
- `conf`: `H` (certain), `M` (probable), `L` (ambiguous). Use `L` whenever two menu senses are truly possible.
- `note`: short reason, required for `L` and `none`, otherwise empty. No tab characters.

Return the file path when done and nothing else.

## Adjudicator (C)

Judge C additionally receives, per row, the answers of A and B (`sense/conf: note`). It writes the same output format
and must decide by the sentence alone.
