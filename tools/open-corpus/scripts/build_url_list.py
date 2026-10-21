#!/usr/bin/env python3
"""Discover candidate source documents for the scaled corpus and write them to sources/discovered/*.tsv (then `assemble`).

  build_url_list.py gutenberg   candidate books from the local Gutenberg cache: landing page checks + publication-year evidence
  build_url_list.py voa         VOA Learning English article ids from the staff-written zone listings
  build_url_list.py nws         NOAA National Weather Service pages (JetStream and safety)
  build_url_list.py usgs        USGS Water Science School pages
  build_url_list.py nps         National Park Service nature and history pages of many parks
  build_url_list.py assemble    merge everything with the pilot documents into sources/urls.tsv (NOS is dropped)

Only agencies with an explicit public-domain statement on their own site are used (see terms.tsv). Listing pages are
fetched politely (one request per second); the documents themselves are fetched by `harvest.py fetch`.
Gutenberg rule: the author died in 1930 or earlier, the book is English with no translator, the landing page states
"Public domain in the USA", and a publication year of 1930 or earlier is supported by Open Library or by the book's own front matter.
"""
import argparse
import collections
import html
import json
import os
import random
import re
import sys
import time
import urllib.request

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import BUILD, CACHE, SEED, SOURCES, read_tsv, write_tsv
from gutenberg_evidence import front_years, open_library

UA = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0 Safari/537.36"
OLD_CACHE = os.path.join(os.environ.get("TEMP", ""), "wordsense-nounverb", "cache")
DISC = os.path.join(SOURCES, "discovered")
COLUMNS = ["doc_id", "url_key", "family", "kind", "url", "pages", "title", "author", "year", "license_basis", "terms_url"]
GUT_TERMS = "https://www.gutenberg.org/policy/license.html"


def get(url, retries=3):
    for i in range(retries):
        try:
            return urllib.request.urlopen(urllib.request.Request(url, headers={"User-Agent": UA}), timeout=60).read().decode("utf-8", "replace")
        except Exception as e:
            err = e
            time.sleep(2 * (i + 1))
    raise RuntimeError(f"{url}: {err}")


def links(url, base):
    h = get(url)
    return {(base + l if l.startswith("/") else l).split("?")[0].split("#")[0] for l in re.findall(r'href="([^"#]+)"', h)}


def crawl(seeds, pattern, base, limit, delay=0.7):
    rx = re.compile(pattern)
    seen, queue, found = set(seeds), list(seeds), []
    while queue and len(found) < limit:
        u = queue.pop(0)
        try:
            ls = links(u, base)
        except Exception as e:
            print("  skip", u, e)
            continue
        time.sleep(delay)
        for l in sorted(ls):
            if rx.fullmatch(l) and l not in seen:
                seen.add(l)
                queue.append(l)
                found.append(l)
    return found


def landing(book):
    path = os.path.join(CACHE, "raw", f"gutenberg-{book}.landing.html")
    if not os.path.exists(path):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        open(path, "w", encoding="utf-8").write(get(f"https://www.gutenberg.org/ebooks/{book}"))
        time.sleep(1)
    t = re.sub(r"(?is)<(script|style).*?</\1>", " ", open(path, encoding="utf-8").read())
    return re.sub(r"\s+", " ", html.unescape(re.sub(r"<[^>]+>", " ", t)))


def cmd_gutenberg(args):
    os.makedirs(DISC, exist_ok=True)
    pilot = {r["doc_id"] for r in read_tsv(os.path.join(SOURCES, "urls.tsv")) if r["family"] == "gutenberg"}
    out, stats = [], collections.Counter()
    ids = sorted(int(f[2:-4]) for f in os.listdir(OLD_CACHE) if re.fullmatch(r"pg\d+\.txt", f))
    for book in ids:
        doc = f"gutenberg-{book}"
        raw_path = os.path.join(CACHE, "raw", f"{doc}.txt")
        if not os.path.exists(raw_path):
            os.makedirs(os.path.dirname(raw_path), exist_ok=True)
            with open(os.path.join(OLD_CACHE, f"pg{book}.txt"), "rb") as src, open(raw_path, "wb") as dst:
                dst.write(src.read())
        try:
            t = landing(book)
        except Exception as e:
            stats["landing failed"] += 1
            continue
        a = re.search(r"Author (.*?) (?:Title|Illustrator|Editor|Translator|Contributor)", t)
        ti = re.search(r"Title (.*?) (?:Note|Credits|Author|Alternative|Language|Original Publication)", t)
        author = a.group(1).strip() if a else ""
        life = re.search(r"(\d{4})\??-(\d{4})", author)
        died = int(life.group(2)) if life else None
        if re.search(r"\bTranslator\b", t[:2500]):
            stats["translator"] += 1
            continue
        if not re.search(r"Language English", t) or not re.search(r"Copyright Public domain in the USA", t):
            stats["not English / not PD in USA"] += 1
            continue
        if died is None or died > 1930 or not author or "System.Object" in author or re.search(r"(?i)various|unknown|anonymous", author):
            stats["author died after 1930 or unknown"] += 1
            continue
        title = (ti.group(1).strip() if ti else "")[:120]
        years = front_years(book)
        url, best, _ = open_library(title, author.split(",")[0])
        time.sleep(1)
        ol = best["first_publish_year"] if best else None
        if ol and ol <= 1930:
            year, evid = ol, f"Open Library first_publish_year {ol}"
        elif years:
            year, evid = max(int(y) for y in years), "front matter shows " + ",".join(years)
        else:
            stats["no publication-year evidence"] += 1
            continue
        if year > 1930:
            stats["year after 1930"] += 1
            continue
        out.append({"doc_id": doc, "url_key": doc, "family": "gutenberg", "kind": "gutenberg",
                    "url": f"https://www.gutenberg.org/cache/epub/{book}/pg{book}.txt", "pages": "", "title": title,
                    "author": re.sub(r",\s*\d{4}\??-\d{4}.*$", "", author), "year": year,
                    "license_basis": f"US public domain: author d. {died}; first published {year} (published 1930 or earlier; {evid}); the book's landing page states 'Public domain in the USA'; Project Gutenberg header, footer and license stripped",
                    "terms_url": GUT_TERMS})
        stats["kept"] += 1
        print(book, title[:50], author[:30], year, evid, flush=True)
    write_tsv(os.path.join(DISC, "gutenberg.tsv"), out, COLUMNS)
    print(dict(stats), "pilot books", len(pilot))


def cmd_voa(args):
    os.makedirs(DISC, exist_ok=True)
    ids = collections.OrderedDict()
    for zone in (987, 1579, 986, 955, 3521, 7468, 5535, 4456, 952):
        empty = 0
        for p in range(1, 40):
            try:
                h = get(f"https://learningenglish.voanews.com/z/{zone}?p={p}")
            except Exception:
                break
            new = [i for _, i in re.findall(r'href="(/a/[^"]*?(\d+)\.html)"', h) if i not in ids]
            for i in dict.fromkeys(new):
                ids[i] = zone
            empty = empty + 1 if not new else 0
            time.sleep(0.6)
            if empty >= 2:
                break
        print("zone", zone, "total ids", len(ids), flush=True)
    rows = [{"doc_id": f"voa-{i}", "url_key": f"voa-{i}", "family": "voa", "kind": "html", "url": f"https://learningenglish.voanews.com/a/{i}.html",
             "pages": "", "title": "", "author": "", "year": "",
             "license_basis": "US federal government work (17 U.S.C. 105): VOA staff-written article; VOA copyright statement: 'All text, audio and video material produced exclusively by the Voice of America is in the public domain'; articles carrying AP/Reuters/AFP/copyright markers are excluded",
             "terms_url": "https://www.voanews.com/p/5338.html"} for i in ids]
    write_tsv(os.path.join(DISC, "voa.tsv"), rows, COLUMNS)


def simple_family(name, urls, family, author, terms, basis, key_fn):
    os.makedirs(DISC, exist_ok=True)
    rows = []
    for u in urls:
        k = key_fn(u)
        rows.append({"doc_id": k, "url_key": k, "family": family, "kind": "html", "url": u, "pages": "", "title": "", "author": author, "year": "",
                     "license_basis": basis, "terms_url": terms})
    write_tsv(os.path.join(DISC, name + ".tsv"), rows, COLUMNS)
    print(name, len(rows))


def cmd_nws(args):
    base = "https://www.noaa.gov"
    jet = crawl(["https://www.noaa.gov/jetstream"], r"https://www\.noaa\.gov/jetstream/[a-z0-9_\-/]+", base, 400)
    jet = [u for u in jet if not re.search(r"/(about|appendix|downloads|contact|learning-lessons)", u)]
    safety = sorted(l for l in links("https://www.weather.gov/safety/", "https://www.weather.gov") if re.fullmatch(r"https://www\.weather\.gov/safety/[a-z\-]+", l))
    basis = "US federal government work (17 U.S.C. 105); NWS disclaimer: 'The information on National Weather Service (NWS) Web pages are in the public domain, unless specifically noted otherwise'"
    simple_family("nws", jet + safety, "noaa-nws", "NOAA National Weather Service", "https://www.weather.gov/disclaimer", basis,
                  lambda u: "nws-safety-" + u.rsplit("/", 1)[1] if "weather.gov/safety" in u else "nws-jetstream-" + u.split("/jetstream/")[1].replace("/", "-"))


def listing(template, article_pattern, base, max_pages, delay=0.7):
    """Collect article links from a paginated listing page (?page=N) until a page adds nothing new."""
    rx, found, stale = re.compile(article_pattern), collections.OrderedDict(), 0
    for n in range(max_pages):
        try:
            ls = links(template.format(n), base)
        except Exception:
            break
        new = [l for l in sorted(ls) if rx.fullmatch(l) and l not in found]
        for l in new:
            found[l] = 1
        stale = stale + 1 if not new else 0
        time.sleep(delay)
        if stale >= 2:
            break
    return list(found)


def cmd_usgs(args):
    base = "https://www.usgs.gov"
    seeds = ["https://www.usgs.gov/special-topics/water-science-school/science/water-cycle", "https://www.usgs.gov/special-topics/water-science-school/science/groundwater-and-water-cycle",
             "https://www.usgs.gov/special-topics/water-science-school/science/earths-water-distribution"]
    urls = crawl(seeds, r"https://www\.usgs\.gov/special-topics/water-science-school/science/[a-z0-9\-]+", base, 500)
    urls = sorted(set(urls) | set(seeds))
    for template, pat in (("https://www.usgs.gov/news/news-releases/all-news-releases?page={}", r"https://www\.usgs\.gov/news/(national|state)-news-release/[a-z0-9\-]+"),
                          ("https://www.usgs.gov/news/science-snippets?page={}", r"https://www\.usgs\.gov/news/science-snippet/[a-z0-9\-]+"),
                          ("https://www.usgs.gov/news/featured-stories?page={}", r"https://www\.usgs\.gov/news/featured-story/[a-z0-9\-]+"),
                          ("https://www.usgs.gov/faqs?page={}", r"https://www\.usgs\.gov/faqs/[a-z0-9\-]+")):
        got = listing(template, pat, base, 40)
        print("usgs listing", template.split("/")[-1], len(got), flush=True)
        urls += got
    urls = sorted(set(urls))
    basis = "US federal government work (17 U.S.C. 105); USGS copyright page: 'USGS-authored or produced data and information are considered to be in the U.S. Public Domain' (third-party items are marked and pages with copyright markers are excluded)"
    simple_family("usgs", urls, "usgs", "US Geological Survey", "https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits", basis,
                  lambda u: "usgs-" + ("wss-" if "water-science-school" in u else u.split("/news/")[1].split("/")[0].replace("-release", "").replace("national-news", "nr").replace("state-news", "sr") + "-" if "/news/" in u else "faq-") + u.rsplit("/", 1)[1][:60])


def cmd_nps(args):
    parks = "yose grca yell zion acad glac romo shen grsm olym seki brca arch cany deva jotr badl bibe ever hale havo kova isro lavo noca crla maca wica jeff sagu gett vick fosu inde alca".split()
    urls = []
    for p in parks:
        for sec in ("nature", "historyculture"):
            seed = f"https://www.nps.gov/{p}/learn/{sec}/index.htm"
            found = crawl([seed], rf"https://www\.nps\.gov/{p}/learn/{sec}/[a-z0-9\-_]+\.htm", "https://www.nps.gov", 14, delay=0.5)
            found = [u for u in found if not u.endswith("/index.htm")]
            urls += found
        print(p, len(urls), flush=True)
    basis = "US federal government work (17 U.S.C. 105); NPS disclaimer: 'material created by the National Park Service and presented on this website, unless otherwise indicated, is generally considered in the public domain' (pages with copyright markers are excluded)"
    simple_family("nps", sorted(set(urls)), "nps", "US National Park Service", "https://www.nps.gov/aboutus/disclaimer.htm", basis,
                  lambda u: "nps-" + u.split("nps.gov/")[1].replace("/learn/", "-").replace("nature/", "").replace("historyculture/", "hc-").replace(".htm", ""))


LIMITS = {"voa": 800, "usgs": 450, "nps": 450, "nws": 400}


def cmd_assemble(args):
    # idempotent: the base is the list of the pilot documents (NOS dropped); the sample of discovered documents is seeded
    pilot_docs = {r["source_id"] for r in read_tsv(os.path.join(BUILD, "pilot_sentences.tsv")) if r["family"] != "noaa-nos"}
    cur = [r for r in read_tsv(os.path.join(SOURCES, "urls.tsv")) if r["doc_id"] in pilot_docs]
    have = {r["doc_id"] for r in cur}
    added = collections.Counter()
    for name in ("gutenberg", "voa", "nws", "usgs", "nps"):
        path = os.path.join(DISC, name + ".tsv")
        if not os.path.exists(path):
            continue
        fresh = [r for r in read_tsv(path) if r["doc_id"] not in have]
        limit = LIMITS.get(name)
        if limit and len(fresh) > limit:  # a seeded random subset keeps the download and annotation effort bounded
            fresh = random.Random(f"{SEED}:urls:{name}").sample(fresh, limit)
        for r in sorted(fresh, key=lambda r: r["doc_id"]):
            cur.append(r)
            have.add(r["doc_id"])
            added[name] += 1
    write_tsv(os.path.join(SOURCES, "urls.tsv"), cur, COLUMNS)
    print(len(cur), "rows; added", dict(added))


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = ap.add_subparsers(dest="cmd", required=True)
    for n, f in (("gutenberg", cmd_gutenberg), ("voa", cmd_voa), ("nws", cmd_nws), ("usgs", cmd_usgs), ("nps", cmd_nps), ("assemble", cmd_assemble)):
        sub.add_parser(n).set_defaults(fn=f)
    args = ap.parse_args()
    args.fn(args)


if __name__ == "__main__":
    main()
