#!/usr/bin/env python3
"""Validate aggregate line coverage from Coverlet Cobertura reports."""

from __future__ import annotations

import argparse
from pathlib import Path
import sys
import xml.etree.ElementTree as ET


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Check aggregate line coverage from Cobertura reports."
    )
    parser.add_argument("results_directory", type=Path)
    parser.add_argument("--minimum-line-coverage", type=float, required=True)
    return parser.parse_args()


def collect_lines(results_directory: Path) -> tuple[int, int, int]:
    reports = sorted(results_directory.rglob("coverage.cobertura.xml"))
    if not reports:
        raise SystemExit(f"No Cobertura reports found under {results_directory}.")

    lines: dict[tuple[str, str], int] = {}
    for report in reports:
        root = ET.parse(report).getroot()
        for class_element in root.findall(".//class"):
            class_name = class_element.attrib.get("name", "")
            if not class_name:
                raise SystemExit(f"Invalid class entry in {report}.")

            # Coverlet can emit project-relative and assembly-prefixed filenames
            # for the same instrumented class across test projects.
            for line_element in class_element.findall("./lines/line"):
                line_number = line_element.attrib.get("number")
                hits = line_element.attrib.get("hits")
                if line_number is None or hits is None:
                    raise SystemExit(f"Invalid line entry in {report}.")

                key = (class_name, line_number)
                lines[key] = max(lines.get(key, 0), int(hits))

    covered = sum(hits > 0 for hits in lines.values())
    return covered, len(lines), len(reports)


def main() -> int:
    args = parse_args()
    if args.minimum_line_coverage < 0 or args.minimum_line_coverage > 100:
        raise SystemExit("The minimum line coverage must be between 0 and 100.")

    covered, total, report_count = collect_lines(args.results_directory)
    if total == 0:
        raise SystemExit("Cobertura reports contain no executable lines.")

    actual = covered / total * 100
    print(
        f"Coverage: {covered}/{total} lines = {actual:.2f}%; "
        f"minimum = {args.minimum_line_coverage:.2f}%; reports = {report_count}."
    )
    if actual + 1e-9 < args.minimum_line_coverage:
        print("Coverage gate failed.", file=sys.stderr)
        return 1

    print("Coverage gate passed.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
