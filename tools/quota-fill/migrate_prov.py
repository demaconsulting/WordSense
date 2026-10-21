"""One-off migration of the quota provenance files of wave 1: licence "Public domain in the United States" and the
per-row column licence_basis (the licence basis of the document). Safe to run again."""
import csv
import glob
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import fill
import pool
import qf


def main():
    _rows, docs = pool.load_pool()
    for path in sorted(glob.glob(os.path.join(qf.CORPUS, "provenance", "quota-*.tsv"))):
        with open(path, encoding="utf-8", newline="") as handle:
            lines = handle.read().split("\n")
        header = lines[0].split("\t")
        if "licence_basis" in header:
            continue
        li, si, ki, ci = header.index("licence"), header.index("source"), header.index("source_key"), header.index("contributor")
        out = ["\t".join(header + ["licence_basis"])]
        for line in lines[1:]:
            if not line:
                continue
            p = line.split("\t")
            source, key = p[si], p[ki]
            if source == "gutenberg":
                doc = "gutenberg-" + key.split("#")[0][2:]
                p[li] = fill.PD_US
                p[ci] = p[ci].replace("; US public domain, published 1930 or earlier)", "; published 1930 or earlier)")
                basis = fill.licence_basis(docs[doc], "gutenberg")
            elif source in ("voa", "usgs", "nps", "nws"):
                doc = key.split("#")[0]
                p[li] = fill.PD_US
                basis = fill.licence_basis(docs[doc], "noaa-nws" if source == "nws" else source)
            elif source == "tatoeba-cc0":
                basis = fill.licence_basis(docs["tatoeba-cc0"], "tatoeba")
            else:
                basis = "project-authored (MIT); no third-party text"
            out.append("\t".join(p + [basis]))
        with open(path, "w", encoding="utf-8", newline="\n") as handle:
            handle.write("\n".join(out) + "\n")
        print("migrated", os.path.basename(path), len(out) - 1)


if __name__ == "__main__":
    main()
