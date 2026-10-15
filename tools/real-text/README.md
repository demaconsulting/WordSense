# Real-Text Harvest

`harvest.py` (Python standard library only) extracts candidate real sentences of at most 35 words from openly
licensed or public-domain sources. Raw downloads go to `cache/` (git-ignored); only extracted sentences are used.

## Sources

- Tatoeba English exports (CC0 subset, and CC BY 2.0 FR with the contributor recorded).
- Project Gutenberg works listed in `gutenberg-works.tsv` (US public domain; the header and licence block are stripped).
- Wikipedia and Universal Dependencies English EWT sentences (CC BY-SA, URL recorded), single sentences only.
- Web excerpts (licence `web-excerpt`) are a last resort for senses that no other source covers. `harvest.py` does not
  fetch them; a single short excerpt is entered by hand into a candidates file with its URL, and the builder rejects a
  web excerpt without an `http(s)` URL (`validate-provenance`).

## Usage

```text
python tools/real-text/harvest.py fetch --gutenberg
python tools/real-text/harvest.py extract --corpus corpus --targets tools/real-text/targets.tsv --out audit-work/real/candidates.tsv
python tools/real-text/harvest.py wiki --corpus corpus --lemma LEMMA --query QUERY --pattern REGEX --out audit-work/real/wiki.tsv
python tools/real-text/harvest.py stats --targets tools/real-text/targets.tsv
python tools/real-text/harvest.py eval-sample --corpus corpus --lemmas lemmas.txt --out audit-work/real/candidates-eval.tsv
```

Then use the builder commands `real-judge-export`, `real-judge-adjudicate`, `real-judge-merge` and `real-import`
(or `real-eval-import`) to judge the candidates blind and write the corpus and its provenance. See the design of the
WordSenseBuilder unit.
