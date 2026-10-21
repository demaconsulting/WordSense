#!/usr/bin/env python3
"""Fetch terms pages and source documents, extract clean text, and write sources.tsv (Python 3.10+, lxml, pypdf).

  harvest.py terms     fetch the licence/terms pages, record SHA-256, date and the key sentence found
  harvest.py fetch     download every document in sources/urls.tsv (and the Tatoeba CC0 export) into cache/
  harvest.py extract   extract clean paragraphs per document into cache/text/, apply the per-document exclusion
                       rules, write sources.tsv (kept documents) and sources_excluded.tsv (rejected documents)

Politeness: browser-like User-Agent, one request at a time, 1 second between requests.
The cache/ folder is not part of the corpus (raw third-party downloads); sources.tsv records the hashes needed to
check that a re-download is identical.
"""
import argparse
import datetime
import html
import json
import os
import re
import shutil
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import CACHE, SOURCES, ROOT, normalize_text, read_tsv, sha256_bytes, sha256_file, sha256_text, write_tsv

UA = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36")
TODAY = datetime.date.today().isoformat()
OLD_CACHE = os.path.join(os.environ.get("TEMP", ""), "wordsense-nounverb", "cache")  # optional local cache to reuse

TERMS = [
    # id, family, url, sentence that must appear on the page (regex)
    ("gutenberg-license", "gutenberg", "https://www.gutenberg.org/policy/license.html",
     r"If you strip the Project Gutenberg license and all references to Project Gutenberg from the text"),
    ("gutenberg-permission", "gutenberg", "https://www.gutenberg.org/policy/permission.html",
     r"The vast majority of Project Gutenberg eBooks are in the public domain in the US"),
    ("us-copyright-105", "federal", "https://www.law.cornell.edu/uscode/text/17/105",
     r"Copyright protection under this title is not available for any work of the United States Government"),
    ("us-copyright-term", "gutenberg", "https://www.copyright.gov/help/faq/faq-duration.html",
     r"(?i)(95 years|public domain)"),
    ("voa-terms", "voa", "https://www.voanews.com/p/5338.html",
     r"All text, audio and video material produced exclusively by the Voice of America is in the public domain"),
    ("usgs-copyrights", "usgs", "https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits",
     r"USGS-authored or produced data and information are considered to be in the U\.S\. Public Domain"),
    ("nps-disclaimer", "nps", "https://www.nps.gov/aboutus/disclaimer.htm",
     r"is generally considered in the public domain"),
    ("nasa-media-guidelines", "nasa", "https://www.nasa.gov/nasa-brand-center/images-and-media/",
     r"generally are not subject to copyright in the United States"),
    ("nws-disclaimer", "noaa-nws", "https://www.weather.gov/disclaimer",
     r"are in the public domain, unless specifically noted otherwise"),
    ("nos-disclaimer", "noaa-nos", "https://oceanservice.noaa.gov/disclaimer.html",
     r"is considered public information and may be distributed or copied, unless specifically noted otherwise"),
    ("faa-web-policies", "faa", "https://www.faa.gov/web_policies/", r"(?i)(copyright|public domain)"),
    ("tatoeba-downloads", "tatoeba", "https://tatoeba.org/en/downloads",
     r"A part of our sentences are also available under CC0 1.0"),
    ("tatoeba-terms", "tatoeba", "https://tatoeba.org/en/terms_of_use", r"(?i)(CC0|CC BY 2\.0 FR)"),
    ("cc0-legalcode", "tatoeba", "https://creativecommons.org/publicdomain/zero/1.0/legalcode", r"(?i)CC0 1\.0 Universal"),
    ("cdc-agency-materials", "cdc", "https://www.cdc.gov/other/agencymaterials.html",
     r"Most of the information on the CDC and ATSDR websites is not subject to copyright"),
]

# Families whose terms were read but whose content is NOT used (owner rule: explicit public-domain statement on the agency's own site).
EXCLUDED_TERMS = {"nasa-media-guidelines", "faa-web-policies", "cdc-agency-materials"}

TATOEBA_CC0 = "https://downloads.tatoeba.org/exports/sentences_CC0.tar.bz2"

# Recorded when the page cannot give the key sentence to a script.
TERMS_NOTES = {
    "faa-web-policies": "NOTE: the page carries no copyright or public-domain statement (FAA content EXCLUDED: statute only, "
                        "no explicit public-domain statement on the agency's own site).",
    "cdc-agency-materials": "NOTE: HTTP 403 to scripted clients, so no hash. Read interactively on 2026-10-19: 'Most of the "
                            "information on the CDC and ATSDR websites is not subject to copyright ... a few exceptions' "
                            "(contractor, grantee and licensed material) and public-domain reuse requires attribution, a "
                            "no-endorsement disclaimer, no change to substantive content and a statement that the "
                            "material is free on the agency site. Conditions beyond 17 U.S.C. 105 plus blocked access: CDC EXCLUDED.",
}


def http_get(url, retries=3):
    last = None
    for attempt in range(retries):
        try:
            req = urllib.request.Request(url, headers={"User-Agent": UA, "Accept": "text/html,application/pdf,*/*"})
            with urllib.request.urlopen(req, timeout=180) as r:
                return r.read()
        except Exception as e:  # report after the last attempt
            last = e
            time.sleep(2 * (attempt + 1))
    raise RuntimeError(f"{url}: {last}")


def visible_text(raw_html):
    t = re.sub(r"(?is)<(script|style).*?</\1>", " ", raw_html)
    t = re.sub(r"<[^>]+>", " ", t)
    return re.sub(r"\s+", " ", html.unescape(t))


def cmd_terms(_):
    rows = []
    os.makedirs(os.path.join(CACHE, "terms"), exist_ok=True)
    for tid, family, url, pattern in TERMS:
        path = os.path.join(CACHE, "terms", tid + ".html")
        row = {"terms_id": tid, "family": family, "url": url, "retrieved": TODAY,
               "status": "excluded" if tid in EXCLUDED_TERMS else "used"}
        try:
            data = http_get(url)
            with open(path, "wb") as f:
                f.write(data)
            text = visible_text(data.decode("utf-8", "replace"))
            m = re.search(pattern, text)
            row.update(sha256=sha256_bytes(data), key_sentence_found="yes" if m else "no",
                       excerpt=(text[max(0, m.start() - 80):m.end() + 120] if m else ""))
        except Exception as e:
            row.update(sha256="", key_sentence_found="fetch-failed", excerpt=str(e)[:200])
        print(tid, row["key_sentence_found"])
        if row["key_sentence_found"] != "yes" and tid in TERMS_NOTES:
            row["excerpt"] = TERMS_NOTES[tid]
        rows.append(row)
        time.sleep(1)
    write_tsv(os.path.join(ROOT, "terms.tsv"), rows,
              ["terms_id", "family", "status", "url", "retrieved", "sha256", "key_sentence_found", "excerpt"])


SLOW_HOSTS = {"www.noaa.gov": 1.5, "www.weather.gov": 1.5}  # these sites answer fast repeated requests with a bot-challenge page
_host_locks = {}
_locks_guard = __import__("threading").Lock()


def polite_get(url):
    host = url.split("/")[2]
    if host in SLOW_HOSTS:
        with _locks_guard:
            lock = _host_locks.setdefault(host, __import__("threading").Lock())
        with lock:
            data = http_get(url)
            time.sleep(SLOW_HOSTS[host])
    else:
        data = http_get(url)
    if len(data) < 6000 and b"awsWaf" in data:
        raise RuntimeError(f"{url}: bot-challenge page instead of content")
    return data


def copy_or_download(row, raw_path):
    """Returns (origin, retrieved_date)."""
    if os.path.exists(raw_path):
        return None
    url = row["url"]
    if row["kind"] == "gutenberg":
        old = os.path.join(OLD_CACHE, f"pg{row['doc_id'].split('-')[-1]}.txt")
        if os.path.exists(old):
            shutil.copyfile(old, raw_path)
            return "local-cache", datetime.date.fromtimestamp(os.path.getmtime(old)).isoformat()
    data = polite_get(url)
    with open(raw_path, "wb") as f:
        f.write(data)
    time.sleep(0.3)
    return "download", TODAY


def raw_ext(row):
    return {"html": "html", "pdf": "pdf", "gutenberg": "txt"}[row["kind"]]


def fetch_one(row, raw_dir):
    key = row["url_key"]
    raw_path = os.path.join(raw_dir, f"{key}.{raw_ext(row)}")
    got = copy_or_download(row, raw_path)
    if row["kind"] == "gutenberg":
        lp = os.path.join(raw_dir, f"{key}.landing.html")
        if not os.path.exists(lp):
            with open(lp, "wb") as f:
                f.write(http_get(f"https://www.gutenberg.org/ebooks/{key.split('-')[-1]}"))
            time.sleep(0.3)
    return key, raw_path, got


def cmd_fetch(_):
    import concurrent.futures
    rows = read_tsv(os.path.join(SOURCES, "urls.tsv"))
    raw_dir = os.path.join(CACHE, "raw")
    os.makedirs(raw_dir, exist_ok=True)
    log_path = os.path.join(SOURCES, "fetch_log.tsv")
    log = {r["doc_id"]: r for r in read_tsv(log_path)} if os.path.exists(log_path) else {}
    byrow = {r["url_key"]: r for r in rows}
    failed = 0
    with concurrent.futures.ThreadPoolExecutor(max_workers=6) as ex:  # six requests in flight, a short pause after each
        futures = [ex.submit(fetch_one, r, raw_dir) for r in rows]
        for n, fut in enumerate(concurrent.futures.as_completed(futures), 1):
            try:
                key, raw_path, got = fut.result()
            except Exception as e:
                failed += 1
                print("FAILED", e)
                continue
            row = byrow[key]
            if got:
                log[key] = {"doc_id": key, "url": row["url"], "retrieved": got[1], "origin": got[0],
                            "bytes": os.path.getsize(raw_path), "raw_sha256": sha256_file(raw_path)}
            elif key not in log:
                log[key] = {"doc_id": key, "url": row["url"], "retrieved": TODAY, "origin": "existing-cache",
                            "bytes": os.path.getsize(raw_path), "raw_sha256": sha256_file(raw_path)}
            if n % 100 == 0:
                print(f"fetched {n}/{len(rows)} (failed {failed})", flush=True)
                write_tsv(log_path, list(log.values()), ["doc_id", "url", "retrieved", "origin", "bytes", "raw_sha256"])
    # the Tatoeba CC0 export (all languages, one tab separated line per sentence)
    tar_path = os.path.join(raw_dir, "tatoeba-sentences_CC0.tar.bz2")
    if not os.path.exists(tar_path):
        old = os.path.join(OLD_CACHE, "cc0.tar.bz2")
        if os.path.exists(old):
            shutil.copyfile(old, tar_path)
            when, origin = datetime.date.fromtimestamp(os.path.getmtime(old)).isoformat(), "local-cache"
        else:
            with open(tar_path, "wb") as f:
                f.write(http_get(TATOEBA_CC0))
            when, origin = TODAY, "download"
        log["tatoeba-cc0"] = {"doc_id": "tatoeba-cc0", "url": TATOEBA_CC0, "retrieved": when, "origin": origin,
                              "bytes": os.path.getsize(tar_path), "raw_sha256": sha256_file(tar_path)}
    write_tsv(log_path, list(log.values()), ["doc_id", "url", "retrieved", "origin", "bytes", "raw_sha256"])


# ------------------------------------------------------------------ extraction

THIRD_PARTY = re.compile(r"(?i)\b(Associated Press|Reuters|AFP|Agence France|copyright|all rights reserved)\b|\u00a9|\(AP\)")
BOILERPLATE = re.compile(r"(?i)(wrote this story|VOA Learning English|quiz|hear this story|follow us|subscribe|click here|"
                         r"cookie|privacy|share this|sign up|newsletter|contact us|\bpodcast\b)")


PAGE_BOILERPLATE = re.compile(r"(?i)official website|\.gov website|here.s how you know|secure \.gov|^share|last updated|nps app|"
                              r"https?://|enable javascript|skip to|^\W*$")


def tidy(par):
    return normalize_text(par.replace("\u00ad", ""))


def extract_voa(raw, meta):
    import lxml.html
    doc = lxml.html.fromstring(re.sub(r"^\s*<\?xml[^>]*\?>", "", raw.decode("utf-8", "replace")))
    title = (doc.xpath('string(//meta[@property="og:title"]/@content)') or "").strip()
    block = doc.xpath('//div[@id="article-content"]//div[contains(@class,"wsw")]')
    if not block:
        return None, "no article body found", meta
    pars = []
    for p in block[0].iter("p"):
        if any(a.get("class") and ("wsw__embed" in a.get("class") or "media-pholder" in a.get("class"))
               for a in p.iterancestors()) or any(a.tag in ("figure", "figcaption", "aside") for a in p.iterancestors()):
            continue
        t = tidy(p.text_content())
        if not t:
            continue
        if t.startswith("Words in This Story") or set(t) <= {"_"}:
            break
        pars.append(t)
    body = "\n\n".join(pars)
    heads = " ".join(tidy(h.text_content()) for h in block[0].iter("h2"))
    if re.search(r"Words in This Story", heads):
        pass
    if THIRD_PARTY.search(body) or THIRD_PARTY.search(tidy(block[0].text_content())):
        return None, "third-party agency or copyright marker in the article body (AP/Reuters/AFP/copyright)", meta
    m = re.search(r'"datePublished":"([^"]+)"', raw.decode("utf-8", "replace"))
    meta = dict(meta, title=title, author="VOA Learning English staff", year=(m.group(1)[:4] if m else ""))
    return pars, "", meta


def extract_generic_html(raw, meta, container_xpaths, allow_tables=False):
    import lxml.html
    doc = lxml.html.fromstring(re.sub(r"^\s*<\?xml[^>]*\?>", "", raw.decode("utf-8", "replace")))
    title = tidy(doc.xpath('string(//title)'))
    node = None
    for xp in container_xpaths:
        found = doc.xpath(xp)
        if found:
            node = found[0]
            break
    if node is None:
        return None, "no article container found", meta
    skip_tags = {"figure", "figcaption", "aside", "nav", "footer", "form", "button", "header", "ul", "ol", "li"}
    if not allow_tables:
        skip_tags.add("table")
    pars = []
    for p in node.iter("p"):
        if any(a.tag in skip_tags for a in p.iterancestors()):
            continue
        cls = " ".join(a.get("class", "") for a in list(p.iterancestors())[:3])  # the nearest wrappers only
        if re.search(r"(?i)caption|credit|share|related|promo|footer|disclaimer|breadcrumb", cls):
            continue
        t = tidy(p.text_content())
        if t and not PAGE_BOILERPLATE.search(t):
            pars.append(t)
    if THIRD_PARTY.search("\n".join(pars)):
        return None, "third-party or copyright marker in the page text", meta
    meta = dict(meta, title=re.sub(r"\s*[|\u2013-]\s*(NASA|National Weather Service|National Ocean Service|NOAA).*$", "", title))
    return pars, "", meta


def extract_pdf(path, meta):
    from pypdf import PdfReader
    start, end = [int(x) for x in meta["pages"].split("-")]
    reader = PdfReader(path)
    pars = []
    for pg in range(start - 1, end):
        lines = [l.strip() for l in (reader.pages[pg].extract_text() or "").splitlines()]
        keep = []
        for i, line in enumerate(lines):
            words = line.split()
            nxt = lines[i + 1] if i + 1 < len(lines) else ""
            if not words or re.fullmatch(r"[\d\-ivxlc]+", line) or re.match(r"(Figure|Table|Chapter)\s+\d", line):
                continue
            if len(words) <= 3 and not re.search(r"[.!?]$", line):
                continue
            if len(words) <= 8 and not re.search(r"[.,;:!?)\-]$", line) and nxt[:1].isupper():
                continue
            keep.append(line)
        pars.append(" ".join(keep))
    text = tidy(" ".join(pars))
    if THIRD_PARTY.search(text):
        text = re.sub(r"[^.?!]*(?:copyright|\u00a9|permission)[^.?!]*[.?!]", " ", text, flags=re.I)
    return [text], "", dict(meta, title=meta["title"])


def gutenberg_body(raw_text, meta, landing):
    start = re.search(r"\*\*\* ?START OF (?:THE|THIS) PROJECT GUTENBERG EBOOK.*?\*\*\*", raw_text)
    end = re.search(r"\*\*\* ?END OF (?:THE|THIS) PROJECT GUTENBERG EBOOK", raw_text)
    if not start or not end:
        return None, "start/end markers not found", meta
    header = raw_text[:start.start()]
    if re.search(r"(?im)^(Translator|Translated by)\s*:", header) or "Translator" in landing:
        return None, "translation (translator date unverified)", meta
    if not re.search(r"(?im)^Language:\s*English", header):
        return None, "not English", meta
    status = re.search(r"Copyright\s+(Public domain in the USA)", landing)
    if not status:
        return None, "landing page does not state 'Public domain in the USA'", meta
    body = raw_text[start.end():end.start()].replace("\r\n", "\n")
    paras = [normalize_text(re.sub(r"\s*\n\s*", " ", p)) for p in re.split(r"\n\s*\n", body)]
    paras = [p for p in paras if p and not re.search(r"(?i)gutenberg|transcriber|e-?book|\[illustration|etext|produced by", p)]
    meta = dict(meta, landing_copyright="Public domain in the USA (gutenberg.org/ebooks landing page)")
    return paras, "", meta


def cmd_extract(_):
    urls = read_tsv(os.path.join(SOURCES, "urls.tsv"))
    log = {r["doc_id"]: r for r in read_tsv(os.path.join(SOURCES, "fetch_log.tsv"))}
    text_dir = os.path.join(CACHE, "text")
    os.makedirs(text_dir, exist_ok=True)
    kept, excluded = [], []
    for row in urls:
        key = row["url_key"]
        raw_path = os.path.join(CACHE, "raw", f"{key}.{raw_ext(row)}")
        if not os.path.exists(raw_path):
            excluded.append({"doc_id": row["doc_id"], "url": row["url"], "reason": "download failed"})
            continue
        raw = open(raw_path, "rb").read()
        meta = dict(row)
        try:
            pars, why, meta = extract_one(row, key, raw, raw_path, meta)
        except Exception as e:  # a page that cannot be parsed is rejected, not fatal
            pars, why = None, f"extraction error: {type(e).__name__}"
        if pars is None or not pars:
            excluded.append({"doc_id": row["doc_id"], "url": row["url"], "reason": why or "empty after extraction"})
            continue
        text = "\n\n".join(pars)
        with open(os.path.join(text_dir, row["doc_id"] + ".txt"), "w", encoding="utf-8", newline="\n") as f:
            f.write(text + "\n")
        kept.append({
            "source_id": row["doc_id"], "family": row["family"], "title": meta.get("title") or row.get("title", ""),
            "url": row["url"] + (f"#pages={row['pages']}" if row.get("pages") else ""),
            "author": meta.get("author") or row.get("author", ""), "year": meta.get("year") or row.get("year", ""),
            "license_basis": row["license_basis"], "terms_url": row["terms_url"],
            "retrieved": log[key]["retrieved"], "raw_sha256": log[key]["raw_sha256"], "text_sha256": sha256_text(text),
        })
    write_tsv(os.path.join(ROOT, "build", "documents.tsv"), kept,
              ["source_id", "family", "title", "url", "author", "year", "license_basis", "terms_url", "retrieved",
               "raw_sha256", "text_sha256"])
    write_tsv(os.path.join(SOURCES, "sources_excluded.tsv"), excluded, ["doc_id", "url", "reason"])
    print(f"documents kept {len(kept)}, excluded {len(excluded)}")
    for e in excluded[:50]:
        print("  excluded", e["doc_id"], "-", e["reason"])


def extract_one(row, key, raw, raw_path, meta):
    """Returns (paragraphs or None, reason for rejection, meta)."""
    if row["kind"] == "html":
        if row["family"] == "voa":
            return extract_voa(raw, meta)
        if row["family"] == "nps":
            return extract_generic_html(raw, meta, ["//body"], allow_tables=True)
        if row["family"] in ("noaa-nws", "noaa-nos"):
            return extract_generic_html(raw, meta, ["//body"])
        if row["family"] == "usgs":
            return extract_generic_html(raw, meta, ["//main", "//body"])
        return extract_generic_html(raw, meta, ["//main", '//div[@id="content"]', "//article"])
    if row["kind"] == "pdf":
        return extract_pdf(raw_path, meta)
    landing = visible_text(open(os.path.join(CACHE, "raw", f"{key}.landing.html"), encoding="utf-8").read())
    pars, why, meta = gutenberg_body(raw.decode("utf-8", "replace"), meta, landing)
    m = re.search(r"Author\s+(.*?)\s+Title", landing)
    if m:
        meta["author"] = m.group(1)
    return pars, why, meta


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for name, fn in (("terms", cmd_terms), ("fetch", cmd_fetch), ("extract", cmd_extract)):
        sub.add_parser(name).set_defaults(fn=fn)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
