#!/usr/bin/env python3
"""Validate aggregate line coverage from Coverlet Cobertura reports."""

from __future__ import annotations

import argparse
import posixpath
from pathlib import Path
import re
import sys
import xml.etree.ElementTree as ET


WINDOWS_ABSOLUTE_PATH = re.compile(r"^[A-Za-z]:/")


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Check aggregate line coverage from Cobertura reports."
    )
    parser.add_argument("results_directory", type=Path)
    parser.add_argument("--minimum-line-coverage", type=float, required=True)
    return parser.parse_args()


def normalize_source_filename(source: str, filename: str) -> str:
    source = posixpath.normpath(source.replace("\\", "/"))
    filename = posixpath.normpath(filename.replace("\\", "/"))
    if not filename.startswith("/") and not WINDOWS_ABSOLUTE_PATH.match(filename):
        filename = posixpath.normpath(posixpath.join(source, filename))

    lower_filename = filename.casefold()
    for marker in ("/backend/src/", "/src/", "backend/src/", "src/"):
        marker_index = lower_filename.find(marker)
        if marker_index >= 0:
            return filename[marker_index + len(marker) :].casefold()

    return filename.casefold()


def is_quality_source(normalized_filename: str) -> bool:
    path = normalized_filename.casefold()
    segments = path.split("/")
    filename = segments[-1]
    return (
        "obj" not in segments
        and "Migrations".casefold() not in segments
        and not filename.endswith((".g.cs", ".g.i.cs", ".generated.cs"))
    )


def collect_line_statistics(
    results_directory: Path,
) -> tuple[dict[tuple[str, str, str], int], int]:
    reports = sorted(results_directory.rglob("coverage.cobertura.xml"))
    if not reports:
        raise SystemExit(f"No Cobertura reports found under {results_directory}.")

    lines: dict[tuple[str, str, str], int] = {}
    for report in reports:
        root = ET.parse(report).getroot()
        packages_element = root.find("./packages")
        if packages_element is None:
            raise SystemExit(f"Missing packages element in {report}.")

        sources_element = root.find("./sources")
        sources = [
            source.text or ""
            for source in sources_element.findall("./source")
        ] if sources_element is not None else [""]
        source = sources[0]

        for package_element in packages_element.findall("./package"):
            package_name = package_element.attrib.get("name", "")
            if not package_name:
                raise SystemExit(f"Invalid package entry in {report}.")

            classes_element = package_element.find("./classes")
            if classes_element is None:
                raise SystemExit(f"Missing classes element in {report}.")

            for class_element in classes_element.findall("./class"):
                class_name = class_element.attrib.get("name", "")
                filename = class_element.attrib.get("filename", "")
                if not class_name or not filename:
                    raise SystemExit(f"Invalid class entry in {report}.")

                normalized_filename = normalize_source_filename(source, filename)
                if not is_quality_source(normalized_filename):
                    continue

                for line_element in class_element.findall("./lines/line"):
                    line_number = line_element.attrib.get("number")
                    hits = line_element.attrib.get("hits")
                    if line_number is None or hits is None:
                        raise SystemExit(f"Invalid line entry in {report}.")

                    try:
                        hit_count = int(hits)
                    except ValueError as exception:
                        raise SystemExit(
                            f"Invalid hit count in {report}."
                        ) from exception

                    key = (package_name, normalized_filename, line_number)
                    lines[key] = max(lines.get(key, 0), hit_count)


    return lines, len(reports)


def collect_lines(results_directory: Path) -> tuple[int, int, int]:
    lines, report_count = collect_line_statistics(results_directory)
    covered = sum(hits > 0 for hits in lines.values())
    return covered, len(lines), report_count


def calculate_assembly_statistics(
    lines: dict[tuple[str, str, str], int],
) -> dict[str, tuple[int, int, float]]:
    assemblies: dict[str, list[int]] = {}
    for (assembly, _, _), hits in lines.items():
        assemblies.setdefault(assembly, []).append(hits)

    return {
        assembly: (
            sum(hits > 0 for hits in hits_by_assembly),
            len(hits_by_assembly),
            sum(hits > 0 for hits in hits_by_assembly)
            / len(hits_by_assembly)
            * 100,
        )
        for assembly, hits_by_assembly in sorted(assemblies.items())
    }


def main() -> int:
    args = parse_args()
    if args.minimum_line_coverage < 0 or args.minimum_line_coverage > 100:
        raise SystemExit("The minimum line coverage must be between 0 and 100.")

    lines, report_count = collect_line_statistics(args.results_directory)
    covered = sum(hits > 0 for hits in lines.values())
    total = len(lines)
    if total == 0:
        raise SystemExit("Cobertura reports contain no executable lines.")

    actual = covered / total * 100
    print("Coverage by production assembly:")
    for assembly, (assembly_covered, assembly_total, assembly_actual) in (
        calculate_assembly_statistics(lines).items()
    ):
        print(
            f"- {assembly}: {assembly_covered}/{assembly_total} = "
            f"{assembly_actual:.2f}%"
        )

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
