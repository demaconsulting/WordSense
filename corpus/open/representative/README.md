<!-- markdownlint-disable MD013 MD056 MD060 -->

# Representative part

Random sample of English text: **14,980 sentences, 261,641 tokens, 5,310 documents** (Tatoeba CC0, VOA staff-written news, USGS, NPS and NWS pages, Project Gutenberg books published 1930 or earlier). Files: `data/train.tsv`, `data/dev.tsv`, `data/test.tsv`
(document split 80/10/10, hash rule in `../README.md` section 8; format in `../README.md` section 9). Sources, licenses, selection, annotation pipeline, quality and limitations: `../README.md`, `../sources.tsv`, `../quality.md`.

## Rules

1. **This is the only part that may be used for usage statistics** (tag counts per word, frequency priors, sense frequencies) **and for the headline "real text" evaluation** (dev/test). The training split also trains the part-of-speech heads of the encoder models (`train-pos-head`), and the development split selects their settings; no other split or part is read for that.
2. **Never add hand-picked sentences.** The part grows only by the same random sampling procedure (`tools/open-corpus/scripts/select_sentences.py`: filters, exact-match deduplication, cap of 28 sentences per document, seeded sampling with seed 20261019; new draws must record their own seeds and dedupe against everything already in either part).
   A hand-picked sentence belongs in `../targeted/`.
3. Sentences carry no `# selection` or `# query` line; `tools/open-corpus/scripts/validate.py` rejects them.
4. Consensus status (`unanimous`, `disputed`, `unchecked`) works as described in `../README.md`; the second-annotator cross-check is partial (36.25% of tokens `unchecked`).

Why the separation: a deliberately chosen sentence is chosen *because* it is unusual or hard, so mixing such sentences into a random sample would inflate rare senses, lower measured accuracy and distort every count derived from it (`../README.md` section 1a).

License: annotations and selection CC0 1.0 (`../LICENSE`); sentences public domain in the United States or CC0 per source (`../sources.tsv`).
