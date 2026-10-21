#!/usr/bin/env python3
"""Select the pilot sentences: filter, deduplicate and sample deterministically (seed recorded in common.SEED).

Reads  build/documents.tsv, cache/text/*.txt (from harvest.py extract) and the Tatoeba CC0 export in cache/raw/.
Writes build/sentences.tsv (the pilot sentences with tokens) and sources.tsv (documents that contributed sentences).

Sentence filters (all deterministic): 4-40 tokens; starts with a capital letter (after an optional quote or bracket);
ends with . ? or ! (optionally followed by a closing quote or bracket); balanced quotes and brackets; no URLs, no
markup characters, no replacement characters; at most 3 numeric tokens and at most 20% digit characters; at least 60%
alphabetic tokens; not mostly upper case; not a caption (Figure, Table, Chapter, Source); no duplicate (case and
punctuation insensitive) anywhere in the pilot.
"""
import argparse
import bz2
import math
import os
import random
import re
import sys
import tarfile

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import (BUILD, CACHE, PROMPTS, ROOT, SEED, SOURCES, check_tokenization, normalize_text, read_tsv, sha256_text,
                    split_sentences, tokenize, write_tsv)

GROUPS = [("tatoeba", "conversational"), ("voa", "news"), ("technical", "technical"), ("gutenberg", "literary")]
TECH_FAMILIES = {"noaa-nws", "noaa-nos", "usgs", "nps"}


def fewshot_ids():
    path = os.path.join(PROMPTS, "fewshot_ids.txt")
    return {l.strip() for l in open(path, encoding="utf-8") if l.strip() and not l.startswith("#")}


EXAMPLE_IDS = fewshot_ids()
LIGATURES = str.maketrans({"\ufb01": "fi", "\ufb02": "fl", "\ufb00": "ff", "\ufb03": "ffi", "\ufb04": "ffl"})
BAD_CHARS = re.compile(r"[\ufffd_\[\]{}|*<>=~^\\@#\x00-\x1f]")
CAPTION = re.compile(r"^\W*(Figure|Fig\.|Table|Chapter|Source|See|Note|Photo|Credit|Image)\b", re.I)


def acceptable(text, tokens):
    if not 4 <= len(tokens) <= 40:
        return False
    if not re.match(r"""^["'(]?[A-Z]""", text) or not re.search(r"""[.?!]["')]?$""", text):
        return False
    if BAD_CHARS.search(text) or re.search(r"(?i)https?:|www\.|\.com\b|\.org\b|\.gov\b", text) or CAPTION.match(text):
        return False
    if text.count('"') % 2 or text.count("(") != text.count(")"):
        return False
    numeric = sum(1 for t in tokens if re.fullmatch(r"[\d.,:/%$-]*\d[\d.,:/%$-]*", t))
    if numeric > 3 or sum(c.isdigit() for c in text) > 0.2 * len(text.replace(" ", "")):
        return False
    if sum(1 for t in tokens if re.search(r"[A-Za-z]", t)) < 0.6 * len(tokens):
        return False
    letters = [c for c in text if c.isalpha()]
    if letters and sum(c.isupper() for c in letters) > 0.5 * len(letters):
        return False
    if re.search(r"[A-Z][a-z]+(?: [A-Z][a-z]+){5,}", text):  # long title-case runs: headings or lists of names
        return False
    return check_tokenization(text, tokens)


MALFORMED_TOKEN = re.compile(r'[,;:][A-Za-z"]|.["]|["].')


def malformed(tokens):
    """A token with punctuation fused to letters (a source with a missing space) cannot be annotated reliably."""
    return any(MALFORMED_TOKEN.search(t) for t in tokens)


def key_of(text):
    return re.sub(r"[^a-z0-9]", "", text.lower())


def doc_candidates(doc_id, text):
    out, pos = [], 0
    for paragraph in text.split("\n\n"):
        for sent in split_sentences(paragraph.translate(LIGATURES)):
            sent = normalize_text(sent)
            pos += 1
            toks = tokenize(sent)
            if f"{doc_id}-s{pos:04d}" not in EXAMPLE_IDS and acceptable(sent, toks):
                out.append({"pos": pos, "text": sent, "tokens": toks})
    return out


def tatoeba_candidates():
    path = os.path.join(CACHE, "raw", "tatoeba-sentences_CC0.tar.bz2")
    out = []
    with tarfile.open(path, "r:bz2") as tar:
        member = tar.getmembers()[0]
        for line in tar.extractfile(member):
            parts = line.decode("utf-8").rstrip("\r\n").split("\t")
            if len(parts) < 3 or parts[1] != "eng":
                continue
            text = normalize_text(parts[2])
            toks = tokenize(text)
            if f"tatoeba-{parts[0]}" not in EXAMPLE_IDS and acceptable(text, toks):
                out.append({"id": parts[0], "date": parts[3] if len(parts) > 3 else "", "text": text, "tokens": toks})
    out.sort(key=lambda r: int(r["id"]))
    return out


def sample_docs(candidates_by_doc, n, label):
    rng = random.Random(f"{SEED}:{label}")
    docs = sorted(d for d, c in candidates_by_doc.items() if c)
    per_doc = math.ceil(n / len(docs))
    chosen = {}
    for d in docs:
        r = random.Random(f"{SEED}:{d}")
        pool = list(candidates_by_doc[d])
        chosen[d] = sorted(r.sample(pool, min(per_doc, len(pool))), key=lambda c: c["pos"])
    total = sum(len(v) for v in chosen.values())
    flat = [(d, c["pos"]) for d in docs for c in chosen[d]]
    if total > n:
        for d, pos in rng.sample(flat, total - n):
            chosen[d] = [c for c in chosen[d] if c["pos"] != pos]
    elif total < n:
        rest = [(d, c["pos"]) for d in docs for c in candidates_by_doc[d] if c not in chosen[d]]
        for d, pos in rng.sample(rest, min(n - total, len(rest))):
            chosen[d].append(next(c for c in candidates_by_doc[d] if c["pos"] == pos))
            chosen[d].sort(key=lambda c: c["pos"])
    return chosen


def tatoeba_row(c, log):
    sid = f"tatoeba-{c['id']}"
    return {
        "source_id": sid, "family": "tatoeba", "title": f"Tatoeba sentence {c['id']}",
        "url": f"https://tatoeba.org/en/sentences/show/{c['id']}", "author": "Tatoeba contributor (CC0 export)",
        "year": c["date"][:4], "license_basis": "Tatoeba sentence licensed CC0 1.0 (member of the official sentences_CC0 export)",
        "terms_url": "https://tatoeba.org/en/downloads", "retrieved": log["tatoeba-cc0"]["retrieved"],
        "raw_sha256": log["tatoeba-cc0"]["raw_sha256"], "text_sha256": sha256_text(c["text"])}


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--per-group", type=int, default=250)
    args = ap.parse_args()

    docs = {r["source_id"]: r for r in read_tsv(os.path.join(BUILD, "documents.tsv"))}
    log = {r["doc_id"]: r for r in read_tsv(os.path.join(SOURCES, "fetch_log.tsv"))}
    seen, selected, doc_rows, pools = set(), [], {}, {}

    # Tatoeba first: every sentence is its own document.
    tat, tat_keys = [], set()
    for c in tatoeba_candidates():
        if key_of(c["text"]) not in tat_keys:
            tat_keys.add(key_of(c["text"]))
            tat.append(c)
    pools["conversational"] = {f"tatoeba-{c['id']}": [dict(c, pos=0)] for c in tat}
    rng = random.Random(f"{SEED}:tatoeba")
    for c in sorted(rng.sample(tat, args.per_group), key=lambda c: int(c["id"])):
        seen.add(key_of(c["text"]))
        sid = f"tatoeba-{c['id']}"
        selected.append({"group": "conversational", "family": "tatoeba", "source_id": sid, "sent_id": sid,
                         "text": c["text"], "tokens": c["tokens"]})
        doc_rows[sid] = tatoeba_row(c, log)

    for family, group in GROUPS[1:]:
        by_doc, group_keys = {}, set()
        for doc_id, row in sorted(docs.items()):
            fam = row["family"]
            if (family == "technical" and fam in TECH_FAMILIES) or fam == family:
                cands = doc_candidates(doc_id, open(os.path.join(CACHE, "text", doc_id + ".txt"), encoding="utf-8").read())
                uniq = []  # a sentence repeated in several documents of the group is kept once (first document)
                for c in cands:
                    k = key_of(c["text"])
                    if k not in seen and k not in group_keys:
                        group_keys.add(k)
                        uniq.append(c)
                by_doc[doc_id] = uniq
        pools[group] = by_doc
        print(f"{group}: {len(by_doc)} documents, {sum(len(v) for v in by_doc.values())} candidate sentences")
        for doc_id, cs in sample_docs(by_doc, args.per_group, group).items():
            for c in cs:
                seen.add(key_of(c["text"]))
                selected.append({"group": group, "family": docs[doc_id]["family"], "source_id": doc_id,
                                 "sent_id": f"{doc_id}-s{c['pos']:04d}", "text": c["text"], "tokens": c["tokens"]})
            if cs:
                doc_rows[doc_id] = docs[doc_id]

    order = {g: i for i, (_, g) in enumerate(GROUPS)}
    selected.sort(key=lambda r: (order[r["group"]], r["source_id"], r["sent_id"]))
    for i, r in enumerate(selected, 1):
        r["uid"] = f"P{i:04d}"

    # Repair stage (duplicates, and sentences with a token that has punctuation fused to letters): documents of one group may share sentences (boilerplate repeated across pages), and the
    # per-document sampling above only removes duplicates inside a document. Keep the first occurrence (lowest uid),
    # drop later duplicates and draw replacements (same document first, then same group) with a seeded generator.
    # Replacement sentences get the next free uids so that the uids of all other sentences do not change.
    keys, dropped, final = set(), [], []
    for r in selected:
        k = key_of(r["text"])
        (dropped if k in keys or malformed(r["tokens"]) else final).append(r)
        keys.add(k)
    taken = {(r["source_id"], r["sent_id"]) for r in selected}
    next_uid = len(selected) + 1
    for r in dropped:
        rr = random.Random(f"{SEED}:repair:{r['uid']}")
        group_pool = pools[r["group"]]
        options = [(d, c) for d in [r["source_id"]] for c in group_pool[d]
                   if key_of(c["text"]) not in keys and not malformed(c["tokens"]) and (d, f"{d}-s{c['pos']:04d}" if c["pos"] else d) not in taken]
        if not options:
            options = [(d, c) for d in sorted(group_pool) for c in group_pool[d]
                       if key_of(c["text"]) not in keys and not malformed(c["tokens"]) and (d, f"{d}-s{c['pos']:04d}" if c["pos"] else d) not in taken]
        d, c = rr.choice(options)
        sent_id = f"{d}-s{c['pos']:04d}" if c["pos"] else d
        keys.add(key_of(c["text"]))
        taken.add((d, sent_id))
        if d not in doc_rows:
            doc_rows[d] = docs[d] if d in docs else tatoeba_row(c, log)
        final.append({"group": r["group"], "family": doc_rows[d]["family"], "source_id": d, "sent_id": sent_id,
                      "text": c["text"], "tokens": c["tokens"], "uid": f"P{next_uid:04d}"})
        next_uid += 1
    write_tsv(os.path.join(BUILD, "dropped_duplicates.tsv"),
              [{"uid": r["uid"], "sent_id": r["sent_id"], "text": r["text"]} for r in dropped], ["uid", "sent_id", "text"])

    final.sort(key=lambda r: (order[r["group"]], r["source_id"], r["sent_id"]))
    for r in final:
        r["ntokens"] = len(r["tokens"])
        r["tokens"] = " ".join(r["tokens"])
    write_tsv(os.path.join(BUILD, "sentences.tsv"), final,
              ["uid", "sent_id", "source_id", "family", "group", "ntokens", "text", "tokens"])
    counts = {}
    for r in final:
        counts[r["source_id"]] = counts.get(r["source_id"], 0) + 1
    source_rows = [dict({k: doc_rows[d][k] for k in ("source_id", "family", "title", "url", "author", "year", "license_basis",
                                                     "terms_url", "retrieved", "raw_sha256", "text_sha256")}, sentences=n)
                   for d, n in sorted(counts.items())]
    write_tsv(os.path.join(ROOT, "sources.tsv"), source_rows,
              ["source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved",
               "raw_sha256", "text_sha256", "sentences"])
    by_group = {}
    for r in final:
        by_group[r["group"]] = by_group.get(r["group"], 0) + 1
    print("selected", len(final), by_group, "documents", len(source_rows), "duplicates replaced", len(dropped))


if __name__ == "__main__":
    main()
