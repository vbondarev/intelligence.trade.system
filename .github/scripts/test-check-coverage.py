#!/usr/bin/env python3
"""Self-contained regression tests for the Cobertura coverage aggregator."""

from __future__ import annotations

from pathlib import Path
import runpy
from tempfile import TemporaryDirectory


collect_lines = runpy.run_path(
    str(Path(__file__).with_name("check-coverage.py"))
)["collect_lines"]


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
    print("Coverage aggregator tests passed.")
