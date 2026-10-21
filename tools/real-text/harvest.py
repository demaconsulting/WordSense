#!/usr/bin/env python3
"""Harvest candidate real sentences for the WordSense corpus (Python standard library only).

Sources (open or public domain only):
  * Tatoeba English exports (the CC0 subset only; the CC BY sentences are skipped).
  * Project Gutenberg works from the allow-list gutenberg-works.tsv (US public domain; the Project Gutenberg
    header and licence block are stripped, only the sentences between the START and END markers are read).

Raw downloads go to tools/real-text/cache/ (git-ignored). Only extracted candidate sentences are written, to a
work directory, and only the sentences accepted by the blind judges reach the corpus.

Usage:
  harvest.py fetch [--gutenberg]
  harvest.py extract --corpus DIR --targets FILE --out FILE [--seen FILE] [--seed N]
  harvest.py eval-sample --corpus DIR --lemmas FILE --out FILE [--per-source N] [--seed N] [--seen FILE]
  harvest.py stats --targets FILE   (hits per target and source, no output file)

Candidate file columns (tab separated, with header):
  cand_id lemma hint sentence source licence source_key url contributor
"""
import argparse
import bz2
import csv
import hashlib
import io
import json
import os
import random
import re
import sys
import tarfile
import time
import urllib.parse
import urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
CACHE = os.path.join(HERE, "cache")
TATOEBA_DETAILED = "https://downloads.tatoeba.org/exports/per_language/eng/eng_sentences_detailed.tsv.bz2"
TATOEBA_CC0 = "https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2"
GUTENBERG = "https://www.gutenberg.org/cache/epub/{0}/pg{0}.txt"
USER_AGENT = "WordSense-corpus-harvest/1.0 (https://github.com/demaconsulting)"

ABBREVIATIONS = ("Mr", "Mrs", "Ms", "Dr", "St", "Mt", "Messrs", "Col", "Capt", "Gen", "Lieut", "Prof", "Rev", "Hon", "No", "Vol", "viz", "vs", "Sr", "Jr")


def download(url, path):
    if os.path.exists(path) and os.path.getsize(path) > 0:
        return True
    os.makedirs(os.path.dirname(path), exist_ok=True)
    request = urllib.request.Request(url, headers={"User-Agent": USER_AGENT})
    try:
        with urllib.request.urlopen(request, timeout=120) as response, open(path + ".part", "wb") as out:
            while True:
                block = response.read(1 << 16)
                if not block:
                    break
                out.write(block)
        os.replace(path + ".part", path)
        return True
    except Exception as error:  # report and keep going; the caller lists what was reachable
        print(f"  FAILED {url}: {error}", file=sys.stderr)
        return False


def gutenberg_works():
    works = []
    with open(os.path.join(HERE, "gutenberg-works.tsv"), encoding="utf-8") as f:
        for row in csv.DictReader(f, delimiter="\t"):
            works.append(row)
    return works


def command_fetch(args):
    print("tatoeba detailed:", download(TATOEBA_DETAILED, os.path.join(CACHE, "eng_detailed.tsv.bz2")))
    print("tatoeba cc0:", download(TATOEBA_CC0, os.path.join(CACHE, "cc0.tar.bz2")))
    if args.gutenberg:
        for work in gutenberg_works():
            ok = download(GUTENBERG.format(work["id"]), os.path.join(CACHE, f"pg{work['id']}.txt"))
            print(f"gutenberg pg{work['id']} {work['title']}:", ok)


def read_cc0_ids():
    ids = set()
    with tarfile.open(os.path.join(CACHE, "cc0.tar.bz2"), "r:bz2") as tar:
        for member in tar.getmembers():
            if not member.isfile():
                continue
            with tar.extractfile(member) as f:
                for line in io.TextIOWrapper(f, encoding="utf-8"):
                    parts = line.rstrip("\n").split("\t")
                    if len(parts) >= 3 and parts[1] == "eng":
                        ids.add(parts[0])
    return ids


def tatoeba_sentences():
    """Yield (id, text, username, is_cc0)."""
    cc0 = read_cc0_ids()
    with bz2.open(os.path.join(CACHE, "eng_detailed.tsv.bz2"), "rt", encoding="utf-8") as f:
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) < 4 or parts[1] != "eng":
                continue
            yield parts[0], parts[2], parts[3], parts[0] in cc0


def split_sentences(paragraph):
    pieces = re.split(r'(?<=[.!?])["\']?\s+(?=["\']?[A-Z])', paragraph)
    merged = []
    for piece in pieces:
        if merged and re.search(r"\b(" + "|".join(ABBREVIATIONS) + r")\.$", merged[-1]):
            merged[-1] += " " + piece
        else:
            merged.append(piece)
    return merged


def normalize(text):
    text = (text.replace("\u201c", '"').replace("\u201d", '"').replace("\u2018", "'").replace("\u2019", "'")
            .replace("\u2014", " - ").replace("\u2013", "-").replace("\u00a0", " ").replace("_", ""))
    return re.sub(r"\s+", " ", text).strip()


def gutenberg_sentences(work):
    path = os.path.join(CACHE, f"pg{work['id']}.txt")
    if not os.path.exists(path):
        return
    with open(path, encoding="utf-8", errors="replace") as f:
        text = f.read().replace("\r\n", "\n").replace("\ufeff", "")
    start = re.search(r"\*\*\* ?START OF (THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", text)
    end = re.search(r"\*\*\* ?END OF (THE|THIS) PROJECT GUTENBERG EBOOK", text)
    if not start or not end:
        return
    body = text[start.end():end.start()]
    title = re.search(r"^Title:\s*(.+)$", text, re.M)
    author = re.search(r"^Author:\s*(.+)$", text, re.M)
    work = dict(work)
    work["title"] = title.group(1).strip() if title else work["title"]
    work["author"] = author.group(1).strip() if author else work["author"]
    for index, paragraph in enumerate(re.split(r"\n\s*\n", body)):
        paragraph = normalize(paragraph.replace("\n", " "))
        if len(paragraph) < 20 or paragraph.isupper():
            continue
        for sentence in split_sentences(paragraph):
            yield work, index, sentence.strip()


def acceptable(sentence, minimum, maximum=35):
    words = sentence.split()
    if not minimum <= len(words) <= maximum:
        return False
    if any(c in sentence for c in "{}\t[]<>|*#@") or re.search(r"\d{3,}", sentence):
        return False
    if re.search(r"project gutenberg|gutenberg|licen[sc]e|copyright", sentence, re.I):
        return False
    letters = [c for c in sentence if c.isalpha()]
    if not letters or sum(c.isupper() for c in letters) > 0.35 * len(letters):
        return False
    return sentence[0].isupper() or sentence[0] in "\"'" and len(sentence) > 1 and sentence[1].isupper()


def plain_key(sentence):
    return re.sub(r"[^a-z0-9]+", " ", sentence.lower()).strip()


def known_sentences(corpus):
    """Plain-text keys of every sentence already in the corpus, including the dropped rows."""
    known = set()
    for folder in ("anchors", "holdout", "realtext"):
        directory = os.path.join(corpus, folder)
        if not os.path.isdir(directory):
            continue
        for name in os.listdir(directory):
            if name.endswith(".tsv"):
                with open(os.path.join(directory, name), encoding="utf-8") as f:
                    for line in f:
                        parts = line.rstrip("\n").split("\t")
                        if len(parts) >= 3:
                            known.add(plain_key(parts[2].replace("{", "").replace("}", "")))
    dropped = os.path.join(corpus, "audit", "dropped-rows.tsv")
    if os.path.exists(dropped):
        with open(dropped, encoding="utf-8") as f:
            for line in f:
                parts = line.rstrip("\n").split("\t")
                if len(parts) >= 6:
                    known.add(plain_key(parts[5].replace("{", "").replace("}", "")))
    return known


def read_seen(path):
    seen = set()
    if path and os.path.exists(path):
        with open(path, encoding="utf-8") as f:
            for line in f:
                parts = line.rstrip("\n").split("\t")
                if parts and parts[0] != "cand_id":
                    seen.add(parts[0])
    return seen


def cand_id(lemma, braced):
    return hashlib.sha256(f"{lemma}|{braced}".encode("utf-8")).hexdigest()[:12]


def read_targets(path):
    targets = []
    with open(path, encoding="utf-8") as f:
        for row in csv.DictReader((l for l in f if not l.startswith("#")), delimiter="\t"):
            flags = re.I if "i" in row["flags"] else 0
            targets.append({
                "lemma": row["lemma"],
                "hint": row["hint"],
                "word": re.compile(r"\b(?:" + row["pattern"] + r")\b", flags),
                "ctx": re.compile(row["ctx"], re.I) if row["ctx"] else None,
                "cap": int(row["cap"]),
            })
    return targets


def sources(include_gutenberg=True):
    """Yield (sentence, source, licence, source_key, url, contributor). Only CC0 and US public-domain text is read."""
    for sid, text, user, is_cc0 in tatoeba_sentences():
        if is_cc0 and acceptable(text, 4):
            yield (text, "tatoeba-cc0", "CC0 1.0", sid, f"https://tatoeba.org/en/sentences/show/{sid}", user or "unknown")
    if include_gutenberg:
        for work in gutenberg_works():
            for info, index, sentence in gutenberg_sentences(work):
                if acceptable(sentence, 6):
                    yield (sentence, "gutenberg", "Public domain in the United States", f"pg{info['id']}#p{index}",
                           f"https://www.gutenberg.org/ebooks/{info['id']}", f"{info['author']}: {info['title']}")


def brace(sentence, match):
    return sentence[:match.start()] + "{" + match.group(0) + "}" + sentence[match.end():]


def quota_pick(rows, cap, rng):
    """A third from each of the two sources first, then random from the rest."""
    by_source = {"tatoeba-cc0": [], "gutenberg": []}
    for row in rows:
        by_source[row["source"]].append(row)
    for group in by_source.values():
        rng.shuffle(group)
    picked = by_source["tatoeba-cc0"][:max(1, cap // 3)] + by_source["gutenberg"][:max(1, cap // 3)]
    rest = by_source["tatoeba-cc0"][max(1, cap // 3):] + by_source["gutenberg"][max(1, cap // 3):]
    rng.shuffle(rest)
    picked.extend(rest[:max(0, cap - len(picked))])
    return picked[:cap]


HEADER = ["cand_id", "lemma", "hint", "sentence", "source", "licence", "source_key", "url", "contributor"]


def write_candidates(path, rows):
    os.makedirs(os.path.dirname(os.path.abspath(path)), exist_ok=True)
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\t".join(HEADER) + "\n")
        for row in rows:
            f.write("\t".join(row[c] for c in HEADER) + "\n")


def command_extract(args):
    rng = random.Random(args.seed)
    known = known_sentences(args.corpus)
    seen = read_seen(args.seen)
    targets = read_targets(args.targets)
    hits = [[] for _ in targets]
    taken = set()
    for sentence, source, licence, key, url, contributor in sources(not args.no_gutenberg):
        plain = plain_key(sentence)
        if plain in known or plain in taken:
            continue
        for i, target in enumerate(targets):
            matches = list(target["word"].finditer(sentence))
            if len(matches) != 1 or (target["ctx"] and not target["ctx"].search(sentence)):
                continue
            hits[i].append({"lemma": target["lemma"], "hint": target["hint"], "plain": plain,
                            "sentence": brace(sentence, matches[0]), "source": source, "licence": licence,
                            "source_key": key, "url": url, "contributor": contributor})
    out = []
    # Specific (enriched) targets pick first so that the generic 'any' sample cannot consume their rare sentences
    for target, rows in sorted(zip(targets, hits), key=lambda pair: pair[0]["hint"] == "any"):
        fresh = [r for r in rows if cand_id(r["lemma"], r["sentence"]) not in seen and r["plain"] not in taken]
        picked = quota_pick(fresh, target["cap"], rng)
        counts = {}
        for r in rows:
            counts[r["source"]] = counts.get(r["source"], 0) + 1
        print(f"{target['lemma']:10} {target['hint']:14} hits {counts} picked {len(picked)}")
        for r in picked:
            if r["plain"] in taken:
                continue
            taken.add(r["plain"])
            r["cand_id"] = cand_id(r["lemma"], r["sentence"])
            out.append(r)
    write_candidates(args.out, out)
    print(f"wrote {len(out)} candidates to {args.out}")


def command_stats(args):
    targets = read_targets(args.targets)
    counts = [dict() for _ in targets]
    for sentence, source, *_ in sources(not args.no_gutenberg):
        for i, target in enumerate(targets):
            if len(list(target["word"].finditer(sentence))) == 1 and (not target["ctx"] or target["ctx"].search(sentence)):
                counts[i][source] = counts[i].get(source, 0) + 1
    for target, c in zip(targets, counts):
        print(target["lemma"], target["hint"], c)


def command_eval_sample(args):
    """Random sentences containing a form of each lemma, taken from the forms table (blind sampling)."""
    rng = random.Random(args.seed)
    known = known_sentences(args.corpus)
    seen = read_seen(args.seen)
    lemmas = [l.strip() for l in open(args.lemmas, encoding="utf-8") if l.strip() and not l.startswith("#")]
    forms = {}
    with open(os.path.join(args.corpus, "forms", "speech.tsv"), encoding="utf-8") as f:
        next(f)
        for line in f:
            parts = line.rstrip("\n").split("\t")
            if len(parts) >= 2 and parts[1] in lemmas:
                forms.setdefault(parts[1], set()).add(parts[0])
    patterns = {l: re.compile(r"\b(?:" + "|".join(sorted(map(re.escape, forms.get(l, {l})), key=len, reverse=True)) + r")\b", re.I) for l in lemmas}
    pools = {(l, s): [] for l in lemmas for s in ("tatoeba", "gutenberg")}
    taken = set()
    for sentence, source, licence, key, url, contributor in sources(True):
        plain = plain_key(sentence)
        if plain in known or plain in taken:
            continue
        for lemma, pattern in patterns.items():
            matches = list(pattern.finditer(sentence))
            if len(matches) == 1:
                pools[(lemma, "gutenberg" if source == "gutenberg" else "tatoeba")].append(
                    {"lemma": lemma, "hint": "eval", "plain": plain, "sentence": brace(sentence, matches[0]), "source": source,
                     "licence": licence, "source_key": key, "url": url, "contributor": contributor})
    out = []
    for (lemma, kind), rows in pools.items():
        rng.shuffle(rows)
        count = 0
        for r in rows:
            if count >= args.per_source:
                break
            r["cand_id"] = cand_id(lemma, r["sentence"])
            if r["cand_id"] in seen or r["plain"] in taken:
                continue
            taken.add(r["plain"])
            out.append(r)
            count += 1
        print(f"{lemma:14} {kind:9} pool {len(rows):6} picked {count}")
    write_candidates(args.out, out)
    print(f"wrote {len(out)} candidates to {args.out}")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = parser.add_subparsers(dest="command", required=True)
    p = sub.add_parser("fetch")
    p.add_argument("--gutenberg", action="store_true")
    p = sub.add_parser("extract")
    p.add_argument("--corpus", required=True)
    p.add_argument("--targets", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--seen")
    p.add_argument("--seed", type=int, default=7)
    p.add_argument("--no-gutenberg", action="store_true")
    p = sub.add_parser("stats")
    p.add_argument("--targets", required=True)
    p.add_argument("--no-gutenberg", action="store_true")
    p = sub.add_parser("eval-sample")
    p.add_argument("--corpus", required=True)
    p.add_argument("--lemmas", required=True)
    p.add_argument("--out", required=True)
    p.add_argument("--seen")
    p.add_argument("--per-source", type=int, default=15)
    p.add_argument("--seed", type=int, default=11)
    args = parser.parse_args()
    {"fetch": command_fetch, "extract": command_extract, "stats": command_stats, "eval-sample": command_eval_sample}[args.command](args)


if __name__ == "__main__":
    main()
