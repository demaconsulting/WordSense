<!-- markdownlint-disable MD013 MD056 MD060 -->

# Targeted part

Sentences **deliberately chosen** to exercise specific uses: rare senses, hard conventions, chooser forms, disputed patterns, weak genres and error-driven cases. **So far**: the 32 simplified-technical-English sentences of the challenge set (`data/test.tsv`, `# selection = genre=technical-instructions`, `# query = written:ste-probe`; `../README.md` section 1b); `data/train.tsv` holds only the header.
The format is that of the representative part (`../README.md` section 9: one token per line with the columns `id form lemma upos collapsed feats consensus`) plus two required sentence-level comment lines.

## Rules

1. **Never use it for usage statistics** (tag counts, frequency priors, sense frequencies): the sentences are unusual by design and would skew them. Those come from `../representative/` only.
2. **`data/test.tsv` is a challenge set**: report its scores **separately**, never merged into the headline real-text evaluation, and say how it was selected. `data/train.tsv` may be used to train classifiers.
3. Every sentence needs both lines (`tools/open-corpus/scripts/validate.py` checks them):
   - `# selection = <key>=<value>`, the reason it was chosen, with `<key>` one of `sense`, `convention`, `form`, `genre`, `error`, `pattern`. Examples: `sense=bow#violin-bow`, `convention=passive-vs-adjective`, `form=read`, `genre=informal`, `error=gpt-6.1-sol:passive-vs-adjective`, `pattern=ADJ/NOUN-modifier`.
   - `# query = <how it was found>`, for example the search expression and corpus, the list or rule used, or the model run that produced the error, so the choice can be reproduced and audited.
4. Own split, by document with the same hash bucket as the representative part: `int(SHA-256(source_id)[:8], 16) % 100`; 0-79 train, 80-99 test (`tools/open-corpus/scripts/split_data.py --part targeted`). A document is on the same side of the train/evaluation line in both parts, so `targeted/train` never contains a document that `representative/dev` or `representative/test` evaluates on.
5. Sentences must come from documents listed in `../sources.tsv` (same license bar as the rest: public domain in the United States or CC0, explicit statement recorded), must differ from every sentence of the representative part (exact text and `sent_id`), and are annotated with the same guideline (`../GUIDELINE.md` version 4) and pipeline; the consensus status has the same meaning.

## Example

```text
# sent_id = gutenberg-1234-s17
# source_id = gutenberg-1234
# selection = convention=passive-vs-adjective
# query = regex "\bwas (closed|broken|finished)\b" over the Gutenberg texts in cache/text
# text = The door was closed.
1 The the DET D _ unchecked
...
```

(Illustration of the layout only; the example is not in the data and the remaining tokens are omitted.)

License: annotations and selection CC0 1.0 (`../LICENSE`); sentences public domain in the United States or CC0 per source.
