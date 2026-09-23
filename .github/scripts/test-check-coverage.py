#!/usr/bin/env python3
"""Self-contained regression tests for the Cobertura coverage aggregator."""

from __future__ import annotations

from pathlib import Path
import runpy
from tempfile import TemporaryDirectory


coverage_module = runpy.run_path(
    str(Path(__file__).with_name("check-coverage.py"))
)
collect_lines = coverage_module["collect_lines"]
collect_line_statistics = coverage_module["collect_line_statistics"]
calculate_assembly_statistics = coverage_module["calculate_assembly_statistics"]


def write_report(
    directory: Path,
    name: str,
    package: str,
    source: str,
    class_name: str,
    filename: str,
    line_number: int,
    hits: int,
) -> None:
    report = f"""<?xml version="1.0" encoding="utf-8"?>
<coverage>
  <sources><source>{source}</source></sources>
  <packages>
    <package name="{package}">
      <classes>
        <class name="{class_name}" filename="{filename}">
          <lines><line number="{line_number}" hits="{hits}" /></lines>
        </class>
      </classes>
    </package>
  </packages>
</coverage>
"""
    report_directory = directory / name.removesuffix(".xml")
    report_directory.mkdir()
    (report_directory / "coverage.cobertura.xml").write_text(
        report,
        encoding="utf-8",
    )


def test_quality_source_filtering() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        sources = [
            ("obj/Release/net10.0/Generated.cs", 0),
            ("Persistence/Migrations/Initial.cs", 0),
            ("Persistence/Migrations/Initial.Designer.cs", 0),
            ("Persistence/Migrations/AppModelSnapshot.cs", 0),
            ("Generated.g.cs", 0),
            ("Generated.g.i.cs", 0),
            ("Generated.generated.cs", 0),
            ("Program.cs", 1),
        ]
        for index, (filename, hits) in enumerate(sources):
            write_report(
                directory,
                f"filtered-{index}.xml",
                "Intelligence.TradeSystem.Identity.Migrations",
                "/repo/backend/src/Intelligence.TradeSystem.Identity.Migrations/",
                "Generated",
                filename,
                10,
                hits,
            )

        covered, total, _ = collect_lines(directory)
        assert (covered, total) == (1, 1)


def test_per_assembly_statistics_are_aggregated_independently() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        write_report(
            directory,
            "assembly-a.xml",
            "AssemblyA",
            "/repo/backend/src/AssemblyA/",
            "ClassA",
            "ClassA.cs",
            10,
            1,
        )
        write_report(
            directory,
            "assembly-b.xml",
            "AssemblyB",
            "/repo/backend/src/AssemblyB/",
            "ClassB",
            "ClassB.cs",
            10,
            0,
        )

        lines, _ = collect_line_statistics(directory)
        statistics = calculate_assembly_statistics(lines)

        assert statistics == {
            "AssemblyA": (1, 1, 100.0),
            "AssemblyB": (0, 1, 0.0),
        }
        assert list(statistics) == ["AssemblyA", "AssemblyB"]
        assert (sum(value[0] for value in statistics.values()),
                sum(value[1] for value in statistics.values())) == (1, 2)


def test_same_assembly_different_filename_forms() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        write_report(
            directory,
            "first.xml",
            "AssemblyA",
            "/repo/backend/src/AssemblyA/",
            "ClassA",
            "A.cs",
            10,
            1,
        )
        write_report(
            directory,
            "second.xml",
            "AssemblyA",
            "/repo/backend/src/",
            "ClassA",
            "AssemblyA/A.cs",
            10,
            0,
        )

        assert collect_lines(directory) == (1, 1, 2)


def test_same_class_and_filename_in_different_assemblies() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        write_report(
            directory,
            "assembly-a.xml",
            "AssemblyA",
            "/repo/backend/src/AssemblyA/",
            "Program",
            "Program.cs",
            10,
            1,
        )
        write_report(
            directory,
            "assembly-b.xml",
            "AssemblyB",
            "/repo/backend/src/AssemblyB/",
            "Program",
            "Program.cs",
            10,
            0,
        )

        assert collect_lines(directory) == (1, 2, 2)


def test_same_filename_and_line_in_different_assemblies() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        write_report(
            directory,
            "first.xml",
            "AssemblyA",
            "/repo/backend/src/",
            "Program",
            "Program.cs",
            10,
            0,
        )
        write_report(
            directory,
            "second.xml",
            "AssemblyB",
            "/repo/backend/src/",
            "Program",
            "Program.cs",
            10,
            1,
        )

        assert collect_lines(directory) == (1, 2, 2)


def test_same_source_line_uses_maximum_hits_across_test_projects() -> None:
    with TemporaryDirectory() as temporary_directory:
        directory = Path(temporary_directory)
        write_report(
            directory,
            "uncovered.xml",
            "AssemblyD",
            "/repo/backend/src/AssemblyD/",
            "ClassD",
            "ClassD.cs",
            10,
            0,
        )
        write_report(
            directory,
            "covered.xml",
            "AssemblyD",
            "/repo/backend/src/",
            "ClassD",
            "AssemblyD/ClassD.cs",
            10,
            4,
        )

        assert collect_lines(directory) == (1, 1, 2)


if __name__ == "__main__":
    test_same_assembly_different_filename_forms()
    test_same_class_and_filename_in_different_assemblies()
    test_same_filename_and_line_in_different_assemblies()
    test_same_source_line_uses_maximum_hits_across_test_projects()
    test_quality_source_filtering()
    test_per_assembly_statistics_are_aggregated_independently()
    print("Coverage aggregator tests passed.")
