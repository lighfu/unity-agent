#!/usr/bin/env python3
"""Guard the package.json that ships inside a VPM release zip.

The published listing (https://lighfu.github.io/vpm/index.json) embeds this file
verbatim, once per version, forever. In 0.3.0 through 0.4.8 a Japanese legacyFolders
path was read as CP932 instead of UTF-8 by the local version-bump step, and since the
mangled text was committed back, every release re-mangled it: the field four-folded per
release and eventually grew the listing to 37 MB. Nothing downstream can undo that -
the decode is lossy - so the only place to stop it is before the zip is published.

Usage: verify-package-json.py <release.zip>
"""

import json
import sys
import zipfile

MAX_BYTES = 65536


def fail(msg):
    print(f"::error::{msg}")
    sys.exit(1)


def main():
    if len(sys.argv) != 2:
        fail("usage: verify-package-json.py <release.zip>")
    zip_path = sys.argv[1]

    with zipfile.ZipFile(zip_path) as z:
        hits = [n for n in z.namelist() if n == "package.json"]
        if len(hits) != 1:
            fail(f"expected exactly one package.json at the root of {zip_path}, found {len(hits)}")
        raw = z.read("package.json")

    if len(raw) > MAX_BYTES:
        fail(f"package.json is {len(raw)} bytes - absurdly large, suspect duplicated or mangled fields")

    try:
        text = raw.decode("utf-8")
    except UnicodeDecodeError as e:
        fail(f"package.json is not valid UTF-8: {e}")

    try:
        json.loads(text)
    except json.JSONDecodeError as e:
        fail(f"package.json is not valid JSON: {e}")

    bad = sorted({c for c in text if ord(c) > 127})
    if bad:
        shown = " ".join(f"{c}(U+{ord(c):04X})" for c in bad[:20])
        more = f" (+{len(bad) - 20} more)" if len(bad) > 20 else ""
        fail(
            f"package.json contains non-ASCII characters: {shown}{more}. "
            "Mojibake here is invisible at first - the initial CP932 round-trip changed "
            "151 bytes to 196 - so this check refuses non-ASCII outright. If the characters "
            "are intentional, confirm the file really is UTF-8 and relax this check on purpose."
        )

    print(f"package.json OK: {len(raw)} bytes, ASCII-only, valid JSON")


if __name__ == "__main__":
    main()
