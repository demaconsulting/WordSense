#!/usr/bin/env python3
"""Prepare the external gold files of the tagger evaluation from UD English-EWT development and test (evaluation only).

  prepare_tagger_eval.py [--ud-dir tools/open-corpus/cache] [--data-dir data] [--out-dir tools/open-corpus/work/tagger-eval]

UD data never enters the repository and never trains or ships anything: the two files are read from the git-ignored cache
(download en_ewt-ud-dev.conllu and en_ewt-ud-test.conllu from the pinned commit below by hand), checked against their pinned
SHA-256, mapped to the WordSense lexicon conventions with convention_map.py (possessive determiners, copula `be`, phrasal-verb
particles, prepositions before gerunds) and written to --out-dir as `ud_dev.tsv` and `ud_test.tsv`, one token per line:
`sentence id`, `form`, `tagger tag letter` (N R V J B P C I D A T U O). The builder reads them with

  dotnet run --project tools/WordSense.Builder -- evaluate-tagger --gold tools/open-corpus/work/tagger-eval/ud_dev.tsv

A multi-word token such as "don't" is one token tagged like its first word, the same way the training counts see a contraction.
Commit: 4a4d77f599ea53cc405f85d0cec4b2f14f81d42b of https://github.com/UniversalDependencies/UD_English-EWT (CC BY-SA 4.0).
"""
import argparse
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import CACHE, REPOSITORY, TOOL_ROOT, sha256_file
from convention_map import lexicon_analyses, map_reference

PINNED = {
    "en_ewt-ud-dev.conllu": "39239e0a60db3ae68f4b7036189f11b6692741d10ff8240dd91f74f2760d90f8",
    "en_ewt-ud-test.conllu": "fa024f43dc5da3c5ac02563bc9bd0e974f46cbb1560823976a8f342a37dc494a",
}

LETTERS = {"NOUN": "N", "PROPN": "N", "PRON": "R", "VERB": "V", "ADJ": "J", "ADV": "B", "ADP": "P", "CCONJ": "C", "SCONJ": "C",
           "INTJ": "I", "DET": "D", "AUX": "A", "PUNCT": "U"}


def letter(upos, form):
    if upos == "PART":
        return "T" if form.lower() == "to" else "O"
    return LETTERS.get(upos, "O")


def read_conllu(path):
    """Sentences as lists of row dicts; a multi-word token is one row (its form, the first word's tag), empty nodes are skipped."""
    sentences, rows, words = [], [], {}
    pending, pending_end, skip = None, 0, 0

    def flush():
        nonlocal rows, words
        if rows:
            for r in rows:
                head = words.get(r["head"])
                r["head_feats"] = head["feats"] if head else ""
            sentences.append(rows)
        rows, words = [], {}

    for line in open(path, encoding="utf-8"):
        line = line.rstrip("\n")
        if not line:
            flush()
            skip = 0
            continue
        if line.startswith("#"):
            continue
        f = line.split("\t")
        if "." in f[0]:
            continue
        if "-" in f[0]:
            pending, pending_end = f[1], int(f[0].split("-")[1])
            continue
        number = int(f[0])
        row = {"form": f[1], "lemma": f[2], "upos": f[3], "feats": f[5], "head": f[6], "deprel": f[7]}
        words[f[0]] = row
        if pending is not None:
            rows.append(dict(row, form=pending))
            skip, pending = pending_end, None
        elif number > skip:
            rows.append(row)
    flush()
    return sentences


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--ud-dir", default=CACHE)
    ap.add_argument("--data-dir", default=os.path.join(REPOSITORY, "data"))
    ap.add_argument("--out-dir", default=os.path.join(TOOL_ROOT, "work", "tagger-eval"))
    args = ap.parse_args()
    os.makedirs(args.out_dir, exist_ok=True)
    for name, pinned in PINNED.items():
        path = os.path.join(args.ud_dir, name)
        if not os.path.exists(path):
            print(f"skipped {name}: not found in {args.ud_dir}")
            continue
        digest = sha256_file(path)
        if digest != pinned:
            sys.exit(f"{name}: unexpected SHA-256 {digest}")
        sentences = read_conllu(path)
        lexicon = lexicon_analyses(args.data_dir, {r["form"].lower() for s in sentences for r in s})
        totals = {}
        out = os.path.join(args.out_dir, "ud_" + name.split("-")[2].replace(".conllu", "") + ".tsv")
        with open(out, "w", encoding="utf-8", newline="\n") as w:
            w.write(f"# {name} mapped to the WordSense lexicon conventions (convention_map.py); sentence id, form, tag letter\n")
            for number, rows in enumerate(sentences, 1):
                mapped, counts = map_reference(rows, lexicon)
                for key, value in counts.items():
                    totals[key] = totals.get(key, 0) + value
                for r, upos in zip(rows, mapped):
                    w.write(f"{number}\t{r['form']}\t{letter(upos, r['form'])}\n")
        print(out, len(sentences), "sentences; remapped", totals)


if __name__ == "__main__":
    main()
