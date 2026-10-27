#!/usr/bin/env python3
"""Require docs/test-matrix.md to have an all-pass section for a version."""

from __future__ import annotations

import argparse
import pathlib
import re
import sys


RESULT_RE = re.compile(
    r"^\|\s*(?P<id>[A-Za-z0-9._-]+)\s*\|.*?\|\s*(?P<result>pending|pass|fail)\s*\|",
    re.IGNORECASE,
)


def parse_section(text: str, version: str) -> list[tuple[str, str]]:
    heading = f"## {version}"
    pattern = rf"(?ms)^## {re.escape(version)}\s*\n(.*?)(?=^##\s+\d+\.\d+\.\d+\s*$|\Z)"
    match = re.search(pattern, text)
    if not match:
        raise SystemExit(
            f"docs/test-matrix.md missing section '{heading}'. "
            "Fill and mark every check pass before tagging."
        )

    results: list[tuple[str, str]] = []
    for line in match.group(1).splitlines():
        row = RESULT_RE.match(line.strip())
        if row:
            results.append((row.group("id"), row.group("result").lower()))
    if not results:
        raise SystemExit(
            f"Section '{heading}' has no result rows "
            "(expected | ID | ... | Result | ... | with pending/pass/fail)."
        )
    return results


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--version", required=True, help="manifest version_number")
    parser.add_argument(
        "--matrix",
        default="docs/test-matrix.md",
        help="path to the test matrix markdown file",
    )
    args = parser.parse_args()

    if not re.fullmatch(r"\d+\.\d+\.\d+", args.version):
        raise SystemExit(f"Invalid version: {args.version!r}")

    path = pathlib.Path(args.matrix)
    if not path.is_file():
        raise SystemExit(f"Missing {path}")

    text = path.read_text(encoding="utf-8")
    results = parse_section(text, args.version)

    bad = [(check_id, result) for check_id, result in results if result != "pass"]
    if bad:
        details = ", ".join(f"{check_id}={result}" for check_id, result in bad)
        raise SystemExit(
            f"Version {args.version} is not ready to publish. "
            f"Non-pass checks: {details}"
        )

    print(f"OK: {len(results)} checks for {args.version} are pass")
    return 0


if __name__ == "__main__":
    sys.exit(main())
