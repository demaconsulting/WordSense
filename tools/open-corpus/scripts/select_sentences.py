#!/usr/bin/env python3
"""Select the corpus sentences: filter, deduplicate and sample deterministically (seed recorded in common.SEED).

  select_sentences.py --pilot build/pilot_sentences.tsv --ud-text FILE [FILE ...] --ws-corpus DIR [--per-group 3750] [--cap 30]

Reads build/documents.tsv, cache/text/*.txt (harvest.py extract), the Tatoeba CC0 export in cache/raw/, the pilot sentences
that are kept, and three exclusion sources used only as negative filters (an exact match is dropped, nothing from them is stored):
the sentence texts of UD English-EWT train and dev (CoNLL-U files given with --ud-text) and every sentence-like cell of the
WordSense repository corpus (--ws-corpus, read-only). Matching ignores case and punctuation.
Writes build/sentences.tsv (the selected sentences with tokens), build/dedupe_report.json and sources.tsv.

Sentence filters (all deterministic): 4-40 tokens; starts with a capital letter (after an optional quote or bracket); ends with
. ? or ! (optionally followed by a closing quote or bracket); balanced quotes and brackets; no URLs, markup characters or
replacement characters; no token with punctuation fused to letters; at most 3 numeric tokens and at most 20% digit characters;
at least 60% alphabetic tokens; not mostly upper case; not a caption; no duplicate anywhere in the corpus.
Per group the target is spread evenly over the documents, never more than --cap sentences per document (the pilot sentences
of a document count toward its cap); the pilot sentences are kept.
"""
import argparse
import bz2
import collections
import glob
import json
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
TECH_FAMILIES = {"noaa-nws", "usgs", "nps"}
LIGATURES = str.maketrans({"\ufb01": "fi", "\ufb02": "fl", "\ufb00": "ff", "\ufb03": "ffi", "\ufb04": "ffl"})
BAD_CHARS = re.compile(r"[\ufffd_\[\]{}|*<>=~^\\@#\x00-\x1f]")
CAPTION = re.compile(r"^\W*(Figure|Fig\.|Table|Chapter|Source|See|Note|Photo|Credit|Image)\b", re.I)
MALFORMED_TOKEN = re.compile(r'[,;:][A-Za-z"]|.["]|["].')


def fewshot_ids():
    path = os.path.join(PROMPTS, "fewshot_ids.txt")
    return {l.strip() for l in open(path, encoding="utf-8") if l.strip() and not l.startswith("#")}


EXAMPLE_IDS = fewshot_ids()


def key_of(text):
    return re.sub(r"[^a-z0-9]", "", text.lower())


def malformed(tokens):
    return any(MALFORMED_TOKEN.search(t) for t in tokens)


def acceptable(text, tokens):
    if not 4 <= len(tokens) <= 40:
        return False
    if not re.match(r"""^["'(]?[A-Z]""", text) or not re.search(r"""[.?!]["')]?$""", text):
        return False
    if BAD_CHARS.search(text) or re.search(r"(?i)https?:|www\.|\.com\b|\.org\b|\.gov\b", text) or CAPTION.match(text):
        return False
    if text.count('"') % 2 or text.count("(") != text.count(")") or malformed(tokens):
        return False
    numeric = sum(1 for t in tokens if re.fullmatch(r"[\d.,:/%$-]*\d[\d.,:/%$-]*", t))
    if numeric > 3 or sum(c.isdigit() for c in text) > 0.2 * len(text.replace(" ", "")):
        return False
    if sum(1 for t in tokens if re.search(r"[A-Za-z]", t)) < 0.6 * len(tokens):
        return False
    letters = [c for c in text if c.isalpha()]
    if letters and sum(c.isupper() for c in letters) > 0.5 * len(letters):
        return False
    if re.search(r"[A-Z][a-z]+(?: [A-Z][a-z]+){5,}", text):
        return False
    return check_tokenization(text, tokens)


def doc_candidates(doc_id, text):
    out, pos = [], 0
    for paragraph in text.split("\n\n"):
        for sent in split_sentences(paragraph.translate(LIGATURES)):
            sent = normalize_text(sent)
            pos += 1
            if f"{doc_id}-s{pos:04d}" in EXAMPLE_IDS:
                continue
            toks = tokenize(sent)
            if acceptable(sent, toks):
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


def ud_keys(files):
    keys = set()
    for fn in files:
        for line in open(fn, encoding="utf-8"):
            if line.startswith("# text = "):
                keys.add(key_of(line[9:]))
    return keys


def ws_keys(root):
    keys = set()
    for fn in glob.glob(os.path.join(root, "**", "*.tsv"), recursive=True):
        for line in open(fn, encoding="utf-8", errors="replace"):
            for cell in line.rstrip("\n").split("\t"):
                if cell.count(" ") >= 3 and re.search(r"[.?!][\"')]*$", cell.strip()):
                    keys.add(key_of(cell.replace("{", "").replace("}", "")))
    return keys


def allocate(room, need, rng):
    """Spread `need` over documents as evenly as possible without exceeding each document's room."""
    docs = sorted(d for d, r in room.items() if r > 0)
    lo, hi = 0, max(room.values(), default=0)
    while lo < hi:
        mid = (lo + hi + 1) // 2
        if sum(min(mid, room[d]) for d in docs) <= need:
            lo = mid
        else:
            hi = mid - 1
    quota = {d: min(lo, room[d]) for d in docs}
    rest = need - sum(quota.values())
    extra = [d for d in docs if room[d] > quota[d]]
    rng.shuffle(extra)
    for d in extra[:max(rest, 0)]:
        quota[d] += 1
    return quota


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--pilot", required=True)
    ap.add_argument("--pilot-sources", default=os.path.join(BUILD, "pilot_sources.tsv"), help="sources.tsv rows of the pilot (for its Tatoeba sentences)")
    ap.add_argument("--ud-text", nargs="*", default=[])
    ap.add_argument("--ws-corpus", default="")
    ap.add_argument("--per-group", type=int, default=3750)
    ap.add_argument("--cap", type=int, default=30)
    args = ap.parse_args()

    docs = {r["source_id"]: r for r in read_tsv(os.path.join(BUILD, "documents.tsv"))}
    log = {r["doc_id"]: r for r in read_tsv(os.path.join(SOURCES, "fetch_log.tsv"))}
    report = {"per_group_target": args.per_group, "cap_per_document": args.cap}
    banned_ud = ud_keys(args.ud_text)
    banned_ws = ws_keys(args.ws_corpus) if args.ws_corpus else set()
    report["ud_sentence_keys"], report["wordsense_corpus_sentence_keys"] = len(banned_ud), len(banned_ws)
    print(f"exclusion keys: UD {len(banned_ud)}, WordSense corpus {len(banned_ws)}", flush=True)

    pilot = [r for r in read_tsv(args.pilot) if r["family"] != "noaa-nos"]
    kept_pilot, dropped_pilot = [], []
    for r in pilot:
        k = key_of(r["text"])
        (dropped_pilot if k in banned_ud or k in banned_ws else kept_pilot).append(r)
    report["pilot_sentences_kept"], report["pilot_sentences_dropped_by_exclusion_filters"] = len(kept_pilot), len(dropped_pilot)
    seen = {key_of(r["text"]) for r in kept_pilot}
    group_of = {"tatoeba": "tatoeba", "voa": "voa", "gutenberg": "gutenberg"}
    group_of.update({f: "technical" for f in TECH_FAMILIES})
    selected = []
    doc_rows = {r["source_id"]: r for r in read_tsv(args.pilot_sources) if r["family"] == "tatoeba"}
    for r in kept_pilot:
        selected.append({"group": {"tatoeba": "conversational", "voa": "news", "technical": "technical", "gutenberg": "literary"}[group_of[r["family"]]],
                         "family": r["family"], "source_id": r["source_id"], "sent_id": r["sent_id"], "text": r["text"],
                         "tokens": r["tokens"].split(" "), "pilot": 1})

    for family, group in GROUPS:
        n_pre = sum(1 for s in selected if s["group"] == group)
        need = args.per_group - n_pre
        rng = random.Random(f"{SEED}:scale:{group}")
        if family == "tatoeba":
            pool, keys = [], set()
            dropped = collections.Counter()
            for c in tatoeba_candidates():
                k = key_of(c["text"])
                if k in seen or k in keys:
                    dropped["duplicate"] += 1
                elif k in banned_ud:
                    dropped["UD"] += 1
                elif k in banned_ws:
                    dropped["WordSense corpus"] += 1
                else:
                    keys.add(k)
                    pool.append(c)
            report["tatoeba_candidates_after_filters"] = len(pool)
            report["tatoeba_dropped"] = dict(dropped)
            pick = sorted(rng.sample(pool, need), key=lambda c: int(c["id"]))
            # The sentences chosen here are seen, so a later family cannot select the same text
            seen.update(key_of(c["text"]) for c in pick)
            for c in pick:
                sid = f"tatoeba-{c['id']}"
                selected.append({"group": group, "family": "tatoeba", "source_id": sid, "sent_id": sid, "text": c["text"], "tokens": c["tokens"], "pilot": 0})
                doc_rows[sid] = {
                    "source_id": sid, "family": "tatoeba", "title": f"Tatoeba sentence {c['id']}", "url": f"https://tatoeba.org/en/sentences/show/{c['id']}",
                    "author": "Tatoeba contributor (CC0 export)", "year": c["date"][:4],
                    "license_basis": "Tatoeba sentence licensed CC0 1.0 (member of the official sentences_CC0 export)", "terms_url": "https://tatoeba.org/en/downloads",
                    "retrieved": log["tatoeba-cc0"]["retrieved"], "raw_sha256": log["tatoeba-cc0"]["raw_sha256"], "text_sha256": sha256_text(c["text"])}
            continue
        by_doc, dropped = {}, collections.Counter()
        pre_count = collections.Counter(s["source_id"] for s in selected if s["group"] == group)
        for doc_id, row in sorted(docs.items()):
            fam = row["family"]
            if not ((family == "technical" and fam in TECH_FAMILIES) or fam == family):
                continue
            uniq = []
            for c in doc_candidates(doc_id, open(os.path.join(CACHE, "text", doc_id + ".txt"), encoding="utf-8").read()):
                k = key_of(c["text"])
                if k in seen:
                    dropped["duplicate"] += 1
                elif k in banned_ud:
                    dropped["UD"] += 1
                elif k in banned_ws:
                    dropped["WordSense corpus"] += 1
                else:
                    seen.add(k)
                    uniq.append(c)
            by_doc[doc_id] = uniq
        room = {d: min(len(c), max(args.cap - pre_count.get(d, 0), 0)) for d, c in by_doc.items()}
        usable = sum(room.values())
        report[f"{group}_documents"] = len(by_doc)
        report[f"{group}_candidate_sentences"] = sum(len(c) for c in by_doc.values())
        report[f"{group}_dropped"] = dict(dropped)
        report[f"{group}_room_under_cap"] = usable
        print(f"{group}: {len(by_doc)} documents, {sum(len(c) for c in by_doc.values())} candidates, room under the cap {usable}, need {need}", flush=True)
        quota = allocate(room, min(need, usable), rng)
        short = need - sum(quota.values())
        report[f"{group}_shortfall"] = short
        for d, q in quota.items():
            if q <= 0:
                continue
            r = random.Random(f"{SEED}:scale:{d}")
            for c in sorted(r.sample(by_doc[d], q), key=lambda c: c["pos"]):
                selected.append({"group": group, "family": docs[d]["family"], "source_id": d, "sent_id": f"{d}-s{c['pos']:04d}", "text": c["text"],
                                 "tokens": c["tokens"], "pilot": 0})
            doc_rows[d] = docs[d]
        for s in selected:
            if s["group"] == group and s["source_id"] in docs:
                doc_rows[s["source_id"]] = docs[s["source_id"]]

    order = {g: i for i, (_, g) in enumerate(GROUPS)}
    selected.sort(key=lambda r: (order[r["group"]], r["source_id"], r["sent_id"]))
    for i, r in enumerate(selected, 1):
        r["uid"] = f"S{i:05d}"
        r["ntokens"] = len(r["tokens"])
        r["tokens"] = " ".join(r["tokens"])
    write_tsv(os.path.join(BUILD, "sentences.tsv"), selected, ["uid", "sent_id", "source_id", "family", "group", "pilot", "ntokens", "text", "tokens"])
    counts = collections.Counter(r["source_id"] for r in selected)
    source_rows = [dict({k: doc_rows[d][k] for k in ("source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved", "raw_sha256", "text_sha256")}, sentences=n)
                   for d, n in sorted(counts.items())]
    write_tsv(os.path.join(ROOT, "sources.tsv"), source_rows,
              ["source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved", "raw_sha256", "text_sha256", "sentences"])
    by_group = collections.Counter(r["group"] for r in selected)
    report["selected"], report["by_group"], report["documents"] = len(selected), dict(by_group), len(source_rows)
    with open(os.path.join(BUILD, "dedupe_report.json"), "w", encoding="utf-8", newline="\n") as f:
        json.dump(report, f, indent=1, sort_keys=True)
        f.write("\n")
    print("selected", len(selected), dict(by_group), "documents", len(source_rows))
    print(json.dumps({k: v for k, v in report.items() if "dropped" in k or "shortfall" in k}, indent=1))


if __name__ == "__main__":
    main()
