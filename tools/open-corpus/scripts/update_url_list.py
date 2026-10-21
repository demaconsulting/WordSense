#!/usr/bin/env python3
"""One-off, recorded change of the pinned input list (sources/urls.tsv) for the second pilot draw.

Owner decision of 2026-10-20: keep only agencies whose own site carries an explicit public-domain statement.
FAA and NASA rows are removed (NASA: "generally not subject to copyright"; FAA: no statement). USGS and NPS rows are
added (USGS: "USGS-authored or produced data and information are considered to be in the U.S. Public Domain";
NPS: "material created by the National Park Service ... is generally considered in the public domain").
The statements and URLs are recorded with a hash in terms.tsv.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from common import CACHE, SOURCES, read_tsv, write_tsv

COLUMNS = ["doc_id", "url_key", "family", "kind", "url", "pages", "title", "author", "year", "license_basis", "terms_url"]
USGS_TERMS = "https://www.usgs.gov/information-policies-and-instructions/copyrights-and-credits"
NPS_TERMS = "https://www.nps.gov/aboutus/disclaimer.htm"
USGS = """water-cycle atmosphere-and-water-cycle evaporation-and-water-cycle evapotranspiration-and-water-cycle
freshwater-lakes-and-rivers-and-water-cycle groundwater-flow-and-water-cycle groundwater-storage-and-water-cycle
ice-snow-and-glaciers-and-water-cycle infiltration-and-water-cycle precipitation-and-water-cycle snowmelt-runoff-and-water-cycle
springs-and-water-cycle streamflow-and-water-cycle sun-and-water-cycle surface-runoff-and-water-cycle sediment-and-suspended-sediment
pesticides-groundwater domestic-water-use public-supply-water-use water-qa-why-are-some-lakes-full-algae""".split()
NPS = ["yose/learn/nature/animals", "yose/learn/nature/climate-change", "yose/learn/nature/ecorestoration",
       "yose/learn/nature/environmentalfactors", "yose/learn/nature/naturalfeaturesandecosystems", "yose/learn/nature/plants",
       "yose/learn/nature/research-and-studies", "yose/learn/nature/wildlandfire", "yose/learn/nature/visitor-use",
       "grca/learn/nature/bio-soil", "grca/learn/nature/california-condor", "grca/learn/nature/exotic-tamarisk",
       "grca/learn/nature/granite", "grca/learn/nature/grca-geology", "grca/learn/nature/springs", "grca/learn/nature/puma-eric-york"]


# Corrections found by the Gutenberg year check (gutenberg-books.md): the later, conservative first-publication date is recorded.
# Pygmalion: written 1912, first staged 1913, first English book edition 1916. The Importance of Being Earnest: first staged 1895, first published 1899.
YEAR_FIXES = {"gutenberg-3825": "1916", "gutenberg-844": "1899"}


def main():
    path = os.path.join(SOURCES, "urls.tsv")
    rows = [r for r in read_tsv(path) if r["family"] not in ("faa", "nasa", "usgs", "nps")]
    for r in rows:
        if r["doc_id"] in YEAR_FIXES:
            old = r["year"]
            r["year"] = YEAR_FIXES[r["doc_id"]]
            r["license_basis"] = r["license_basis"].replace(f"first published {old}", f"first published {r['year']}")
    for s in USGS:
        key = "usgs-" + s[:60]
        rows.append(dict(doc_id=key, url_key=key, family="usgs", kind="html",
                         url=f"https://www.usgs.gov/special-topics/water-science-school/science/{s}", pages="", title="",
                         author="US Geological Survey", year="",
                         license_basis="US federal government work (17 U.S.C. 105); USGS copyright page: 'USGS-authored or produced data and information are considered to be in the U.S. Public Domain' (third-party items are marked and pages with copyright markers are excluded)",
                         terms_url=USGS_TERMS))
    for s in NPS:
        key = "nps-" + s.replace("/learn/nature/", "-")
        rows.append(dict(doc_id=key, url_key=key, family="nps", kind="html", url=f"https://www.nps.gov/{s}.htm", pages="",
                         title="", author="US National Park Service", year="",
                         license_basis="US federal government work (17 U.S.C. 105); NPS disclaimer: 'material created by the National Park Service and presented on this website, unless otherwise indicated, is generally considered in the public domain' (pages with copyright markers are excluded)",
                         terms_url=NPS_TERMS))
    write_tsv(path, rows, COLUMNS)
    print(len(rows), "rows")
    # drop the retrieval records and cached copies of the removed families (their content is not used)
    log_path = os.path.join(SOURCES, "fetch_log.tsv")
    log = [r for r in read_tsv(log_path) if not r["doc_id"].startswith(("faa-", "nasa-"))]
    write_tsv(log_path, log, ["doc_id", "url", "retrieved", "origin", "bytes", "raw_sha256"])
    for sub in ("raw", "text"):
        folder = os.path.join(CACHE, sub)
        for name in os.listdir(folder) if os.path.isdir(folder) else []:
            if name.startswith(("faa-", "nasa-")):
                os.remove(os.path.join(folder, name))


if __name__ == "__main__":
    main()
