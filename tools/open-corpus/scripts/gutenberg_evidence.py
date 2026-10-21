#!/usr/bin/env python3
"""Evidence for the recorded first-publication years of the Gutenberg books (for the owner's spot check).

  gutenberg_evidence.py

For each Gutenberg book in sources/urls.tsv: the recorded year (our entry), the author's life dates (from the Gutenberg
landing page cached by harvest.py), the first-publication year Open Library reports for the best-matching work
(Open Library data is CC0; query URL and work key are shown), and any years found in the front matter of the e-book text.
Writes gutenberg-books.md and sources/gutenberg_years.tsv. Needs network access; waits 1 second between requests.
"""
import html
import json
import os
import re
import sys
import time
import urllib.parse
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import CACHE, ROOT, SOURCES, read_tsv, write_tsv

# Hand-written explanations for books where Open Library disagrees or is silent (our reading of the evidence; spot-check these first).
NOTES = {
    "64317": "Open Library's 1920 is a mismatched record. First published April 1925 (Scribner's), from our knowledge; author d. 1940, so the year matters.",
    "73": "Open Library's 1855 is a mismatched record. First published 1895 (serialized 1894); author d. 1900, so any lifetime publication is pre-1931.",
    "42500": "Open Library's 1803 is a mismatched record. Posthumous book edition 1864 (front matter shows 1864); author d. 1862.",
    "23427": "Open Library lists only a modern reprint; front matter shows 1879.",
    "56133": "Open Library lists only a modern reprint; no year in the front matter. Author d. 1888; first edition 1879 (our knowledge). Spot-check.",
    "17625": "Open Library lists only a modern reprint; front matter shows 1920.",
    "52122": "Open Library lists only a modern reprint; front matter shows 1862-1863; author d. 1903.",
    "38870": "Open Library lists only a modern reprint; front matter shows 1899; author d. 1900.",
    "17211": "Open Library lists only a modern reprint; front matter shows 1918.",
    "2871": "Open Library lists only a modern reprint; front matter shows 1868; author d. 1882.",
    "3361": "Open Library lists only a modern reprint; letters written 1737-1768, first published 1774; author d. 1773.",
    "2097": "Recorded 1871 is a front-matter artifact, not the publication year; first published 1890 (our knowledge); author d. 1930.",
    "2360": "Recorded 1922 is a front-matter artifact (a later edition date); first published 1903 (our knowledge); author d. 1922.",
    "7280": "1922 — owner-verified (published 1922, posthumous memoir). Open Library lists only a 1989 reprint and the front matter has no year; the owner confirmed the year.",
    "880": "Open Library's 1877 is a later collection; the essay is in 'My Study Windows' (1871), from our knowledge; author d. 1891.",
    "21959": "Open Library's 1934 is a later edition; front matter shows 1901-1903; first book edition 1902; author d. 1937.",
    "3825": "Written 1912, first staged 1913, first English book edition 1916 (our knowledge); the later date is recorded.",
    "844": "First staged 1895, first published 1899 (our knowledge); the later date is recorded; Open Library shows 1893.",
}

UA = "WordSense-corpus-evidence/1.0 (spot check of publication years)"


def landing_info(book):
    path = os.path.join(CACHE, "raw", f"gutenberg-{book}.landing.html")
    t = re.sub(r"(?is)<(script|style).*?</\1>", " ", open(path, encoding="utf-8").read())
    t = re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", t)))
    author = re.search(r"Author (.*?) (?:Title|Illustrator|Editor)", t)
    lang = re.search(r"Language (\w+)", t)
    copyright_ = re.search(r"Copyright (Public domain in the USA\.?)", t)
    life = re.search(r"(\d{4}|\?)\??-(\d{4})", author.group(1)) if author else None
    return (author.group(1).strip() if author else ""), (life.group(2) if life else ""), (lang.group(1) if lang else ""), bool(copyright_)


def front_years(book):
    text = open(os.path.join(CACHE, "raw", f"gutenberg-{book}.txt"), encoding="utf-8", errors="replace").read()
    m = re.search(r"\*\*\* ?START OF (?:THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", text)
    head = text[m.end(): m.end() + 9000] if m else text[:9000]
    lines = [l for l in head.splitlines() if re.search(r"\b(1[5-9]\d\d)\b", l) and not re.search(r"(?i)gutenberg|release|ebook|e-text", l)]
    years = []
    for l in lines:
        for y in re.findall(r"\b(1[5-9]\d\d)\b", l):
            if int(y) <= 1930 and y not in years:
                years.append(y)
    return years[:4]


def open_library(title, author):
    q = urllib.parse.urlencode({"title": re.split(r"[:;(]", title)[0].strip(), "author": author.split()[-1],
                                "fields": "key,title,author_name,first_publish_year", "limit": 5})
    url = "https://openlibrary.org/search.json?" + q
    try:
        data = json.loads(urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=60).read().decode("utf-8"))
    except Exception as e:
        return url, None, f"query failed: {e}"
    docs = [d for d in data.get("docs", []) if d.get("first_publish_year")]
    want = re.sub(r"[^a-z0-9 ]", "", re.split(r"[:;(]", title)[0].lower()).strip()
    best = None
    for d in docs:
        got = re.sub(r"[^a-z0-9 ]", "", d["title"].lower()).strip()
        if got == want or got.startswith(want) or want.startswith(got):
            best = d
            break
    return url, best, ""


def main():
    used = {r["source_id"] for r in read_tsv(os.path.join(ROOT, "sources.tsv")) if r["family"] == "gutenberg"}
    rows = [r for r in read_tsv(os.path.join(SOURCES, "urls.tsv")) if r["family"] == "gutenberg" and r["doc_id"] in used]
    for r in rows:
        r["title"] = re.split(r" (?:Reading Level|Contents|Series Title|Credits)\b", r["title"])[0].strip()
    out, md = [], []
    for r in rows:
        book = r["doc_id"].split("-")[1]
        author_line, died, lang, pd = landing_info(book)
        years = front_years(book)
        url, best, err = open_library(r["title"], r["author"])
        ol_year = best["first_publish_year"] if best else ""
        ol_key = ("https://openlibrary.org" + best["key"]) if best else ""
        recorded = int(r["year"])
        status = "agrees" if ol_year and abs(int(ol_year) - recorded) <= 3 else ("differs" if ol_year else "no Open Library match")
        out.append({"ebook": book, "title": r["title"], "author": r["author"], "recorded_year": recorded, "author_life_end": died,
                    "open_library_year": ol_year, "open_library_work": ol_key, "front_matter_years": ",".join(years), "note": NOTES.get(book, ""),
                    "landing_copyright_status": "Public domain in the USA" if pd else "NOT STATED", "language": lang, "status": status})
        time.sleep(1)
    write_tsv(os.path.join(SOURCES, "gutenberg_years.tsv"), out, list(out[0].keys()))
    lines = ["# Project Gutenberg books in the corpus: publication-year evidence", "",
             "Generated by `scripts/gutenberg_evidence.py` for the owner's spot check. **Recorded year** is the first-publication year we entered in",
             "`sources/urls.tsv` (and so in `sources.tsv`). **Evidence**: (1) the author's life dates from the book's Gutenberg landing page; (2) the first-publication",
             "year Open Library reports for the best-matching work (Open Library data is CC0; the work page is linked); (3) years printed in the book's own front matter",
             "(title page, preface or edition notes, up to 1930); (4) the landing page's copyright line. A US public-domain claim needs publication in 1930 or earlier",
             "(95 years rule, `terms.tsv`); an author who died before 1931 makes a later first publication impossible except for posthumous editions.", "",
             "| Ebook | Title | Author (life dates) | Recorded year | Open Library first-publication year | Front-matter years | Gutenberg copyright line | Check | Note |", "|---|---|---|---|---|---|---|---|---|"]
    for o in out:
        ol = f"[{o['open_library_year']}]({o['open_library_work']})" if o["open_library_year"] else "none found"
        life = o["author_life_end"] or "?"
        lines.append(f"| [{o['ebook']}](https://www.gutenberg.org/ebooks/{o['ebook']}) | {o['title']} | {o['author']} (d. {life}) | {o['recorded_year']} | {ol} | "
                     f"{o['front_matter_years'] or '-'} | {o['landing_copyright_status']} | {o['status']} | {o['note']} |")
    n_ok = sum(1 for o in out if o["status"] == "agrees")
    lines += ["", f"{len(out)} books; {n_ok} agree with Open Library within 3 years; "
              f"{sum(1 for o in out if o['status'] == 'differs')} differ; {sum(1 for o in out if o['status'] == 'no Open Library match')} have no match "
              "(for those, rely on the author's death year and the front matter, and spot-check by hand).", ""]
    with open(os.path.join(ROOT, "gutenberg-books.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("\n".join(f"{o['ebook']:>6} {o['recorded_year']} OL={o['open_library_year']} died={o['author_life_end']} front={o['front_matter_years']} {o['status']}" for o in out))


if __name__ == "__main__":
    main()
