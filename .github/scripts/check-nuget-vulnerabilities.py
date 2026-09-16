#!/usr/bin/env python3
"""Fail when the .NET SDK reports vulnerable NuGet packages."""

from __future__ import annotations

import argparse
import json
from pathlib import Path


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Check dotnet package list JSON for vulnerability advisories."
    )
    parser.add_argument("report", type=Path)
    return parser.parse_args()


def find_vulnerabilities(document: dict) -> list[tuple[str, str, str, str, dict]]:
    findings: list[tuple[str, str, str, str, dict]] = []
    for project in document.get("projects", []):
        project_path = project.get("path", "<unknown project>")
        for framework in project.get("frameworks", []):
            framework_name = framework.get("framework", "<unknown framework>")
            for package_kind in ("topLevelPackages", "transitivePackages"):
                dependency_kind = (
                    "direct" if package_kind == "topLevelPackages" else "transitive"
                )
                for package in framework.get(package_kind, []):
                    for vulnerability in package.get("vulnerabilities") or []:
                        findings.append(
                            (
                                project_path,
                                framework_name,
                                dependency_kind,
                                package.get("id", "<unknown package>"),
                                {
                                    "version": package.get("resolvedVersion", "<unknown version>"),
                                    "severity": vulnerability.get("severity", "unknown"),
                                    "advisory": vulnerability.get(
                                        "advisoryurl",
                                        vulnerability.get("advisoryUrl", "<unknown advisory>"),
                                    ),
                                },
                            )
                        )
    return findings


def main() -> int:
    args = parse_args()
    payload = args.report.read_bytes()
    encoding = "utf-16" if payload.startswith((b"\xff\xfe", b"\xfe\xff")) else "utf-8-sig"
    document = json.loads(payload.decode(encoding))
    findings = find_vulnerabilities(document)
    if not findings:
        print("NuGet vulnerability check passed: no vulnerable packages reported.")
        return 0

    print(f"NuGet vulnerability check failed: {len(findings)} advisory(s) found.")
    for project, framework, dependency_kind, package, details in findings:
        print(
            f"- {package} {details['version']} ({dependency_kind}, {framework}, "
            f"{details['severity']}): {details['advisory']} [{project}]"
        )
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
