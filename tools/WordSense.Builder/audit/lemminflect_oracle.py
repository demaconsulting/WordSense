"""Oracle of the audit-forms command of WordSense.Builder.

Usage: python lemminflect_oracle.py SAMPLE.tsv VERDICTS.tsv

SAMPLE.tsv has the columns spelling, lemma, part-of-speech letter and features. VERDICTS.tsv gets the same columns plus
ok, bad or skip. LemmInflect (pip install lemminflect) is a development-time checker only: it is not a dependency, nothing
of it ships, its forms are never adopted and this output is not committed.
"""
import sys

from lemminflect import getInflection, getLemma

TAGS = {"Plur": ["NNS"], "Third": ["VBZ"], "Ger": ["VBG"], "Cmp": ["JJR", "RBR"], "Sup": ["JJS", "RBS"], "Past": ["VBD"], "Part": ["VBN", "VBD"]}
UPOS = {"n": "NOUN", "v": "VERB", "a": "ADJ", "r": "ADV"}


def kinds(pos, features):
    f = set(features.split("+"))
    result = []
    if pos == "n" and "Number=Plur" in f:
        result.append("Plur")
    if pos == "v":
        if "Person=3" in f and "Tense=Pres" in f:
            result.append("Third")
        if "VerbForm=Ger" in f:
            result.append("Ger")
        if "Tense=Past" in f:
            result.append("Past")
        if "VerbForm=Part" in f:
            result.append("Part")
    if pos in ("a", "r"):
        if "Degree=Cmp" in f:
            result.append("Cmp")
        if "Degree=Sup" in f:
            result.append("Sup")
    return result


def verdict(spelling, lemma, pos, features):
    ks = kinds(pos, features)
    low = lemma.lower()
    if not ks or pos not in UPOS or " " in low or not low.replace("-", "").replace("'", "").isalpha():
        return "skip"
    word = spelling.lower()
    for kind in ks:
        ok = False
        for tag in TAGS[kind]:
            try:
                if word in [x.lower() for x in getInflection(low, tag=tag)]:
                    ok = True
            except Exception:
                pass
        if not ok:
            try:
                ok = low in [x.lower() for x in getLemma(word, UPOS[pos])]
            except Exception:
                ok = False
        if not ok:
            return "bad"
    return "ok"


def main():
    with open(sys.argv[1], encoding="utf-8") as src, open(sys.argv[2], "w", encoding="utf-8", newline="") as dst:
        for line in src:
            p = line.rstrip("\n").split("\t")
            if len(p) < 4:
                continue
            dst.write("\t".join(p[:4] + [verdict(*p[:4])]) + "\n")


if __name__ == "__main__":
    main()
