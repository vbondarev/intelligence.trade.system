#!/usr/bin/env python3
"""Проверяет структуру repository-owned agent development assets."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
import re
import sys
from urllib.parse import unquote, urlsplit


PROJECT_OWNED_SKILLS = {
    "trade-system-discovery",
    "trade-system-delivery",
    "trade-system-pr-review",
}
REQUIRED_ROUTING_REFERENCES = PROJECT_OWNED_SKILLS
FRONTMATTER_KEY = re.compile(r"^([A-Za-z][A-Za-z0-9_-]*):(?:\s*(.*))?$")
DOUBLE_QUOTED_VALUE = re.compile(r'^"((?:[^"\\]|\\.)*)"(?:\s+#.*)?$')
SINGLE_QUOTED_VALUE = re.compile(r"^'((?:''|[^'])*)'(?:\s+#.*)?$")
MARKDOWN_LINK = re.compile(
    r"!?\[[^\]]*\]\(\s*(?:<([^>]+)>|([^\s)]+))(?:\s+[^)]*)?\)"
)
SKILL_REFERENCE = re.compile(
    r"(?<![\w/])\.agents/skills/([A-Za-z0-9][A-Za-z0-9_.-]*)/SKILL\.md(?![\w])"
)


def read_text(path: Path, root: Path, errors: list[str]) -> str | None:
    try:
        return path.read_text(encoding="utf-8")
    except OSError as exception:
        errors.append(
            f"{path.relative_to(root).as_posix()}: не удалось прочитать файл: "
            f"{exception}"
        )
        return None


def parse_frontmatter(
    text: str,
    relative_path: str,
    errors: list[str],
) -> dict[str, str]:
    lines = text.splitlines()
    if not lines or lines[0] != "---":
        errors.append(f"{relative_path}: frontmatter должен начинаться с '---'.")
        return {}

    closing_line = next(
        (index for index, line in enumerate(lines[1:], start=1) if line == "---"),
        None,
    )
    if closing_line is None:
        errors.append(f"{relative_path}: frontmatter не закрыт.")
        return {}

    values: dict[str, str] = {}
    for line_number, line in enumerate(lines[1:closing_line], start=2):
        if not line.strip() or line[0].isspace():
            continue

        match = FRONTMATTER_KEY.fullmatch(line)
        if match is None:
            errors.append(
                f"{relative_path}:{line_number}: некорректная запись frontmatter."
            )
            continue

        key, value = match.groups()
        if key in values:
            errors.append(f"{relative_path}:{line_number}: повторяется ключ '{key}'.")
            continue
        values[key] = parse_frontmatter_value(
            value or "",
            relative_path,
            line_number,
            errors,
        )

    return values


def parse_frontmatter_value(
    value: str,
    relative_path: str,
    line_number: int,
    errors: list[str],
) -> str:
    value = value.strip()
    if value.startswith('"'):
        match = DOUBLE_QUOTED_VALUE.fullmatch(value)
        if match is None:
            errors.append(
                f"{relative_path}:{line_number}: некорректное quoted значение frontmatter."
            )
            return ""
        try:
            return json.loads(f'"{match.group(1)}"')
        except json.JSONDecodeError:
            errors.append(
                f"{relative_path}:{line_number}: некорректное quoted значение frontmatter."
            )
            return ""
    if value.startswith("'"):
        match = SINGLE_QUOTED_VALUE.fullmatch(value)
        if match is None:
            errors.append(
                f"{relative_path}:{line_number}: некорректное quoted значение frontmatter."
            )
            return ""
        return match.group(1).replace("''", "'")

    value = re.split(r"\s+#", value, maxsplit=1)[0].strip()
    invalid_scalar = (
        not value
        or any(character in value for character in "[]{}\"'")
        or re.search(r":(?:\s|$)", value) is not None
        or value[0] in "!&*|>@`"
        or re.match(r"^[?:-](?:\s|$)", value) is not None
        or value.casefold() in {"null", "~", "true", "false", ".nan", ".inf", "-.inf", "+.inf"}
        or re.fullmatch(r"[-+]?(?:[0-9]+(?:\.[0-9]*)?|\.[0-9]+)(?:[eE][-+]?[0-9]+)?", value)
        is not None
        or "\t" in value
    )
    if invalid_scalar:
        errors.append(
            f"{relative_path}:{line_number}: некорректный unquoted scalar frontmatter."
        )
        return ""
    return value


def is_nonempty_frontmatter_value(value: str) -> bool:
    value = value.strip()
    if len(value) >= 2 and value[0] == value[-1] and value[0] in {"'", '"'}:
        value = value[1:-1].strip()
    return bool(value)


def project_owned_registry_names(text: str) -> list[str]:
    lines = text.splitlines()
    start = next(
        (
            index
            for index, line in enumerate(lines)
            if line.strip() == "## Project-owned skills"
        ),
        None,
    )
    if start is None:
        return []

    names: list[str] = []
    for line in lines[start + 1 :]:
        if line.startswith("## "):
            break
        match = re.match(r"^\|\s*`([^`]+)`\s*\|", line)
        if match is not None:
            names.append(match.group(1))
    return names


def strip_fenced_code_blocks(text: str) -> str:
    result: list[str] = []
    fence: str | None = None
    for line in text.splitlines():
        marker = line.lstrip()
        if marker.startswith("```") or marker.startswith("~~~"):
            current_fence = marker[:3]
            if fence is None:
                fence = current_fence
            elif current_fence == fence:
                fence = None
            continue
        if fence is None:
            result.append(line)
    return "\n".join(result)


def validate_markdown_references(
    root: Path,
    source_path: Path,
    text: str,
    errors: list[str],
) -> set[str]:
    relative_path = source_path.relative_to(root).as_posix()
    text = strip_fenced_code_blocks(text)
    skill_references: set[str] = set()

    for match in MARKDOWN_LINK.finditer(text):
        target = match.group(1) or match.group(2) or ""
        try:
            parsed = urlsplit(target)
        except ValueError:
            errors.append(f"{relative_path}: некорректная внутренняя ссылка: {target}.")
            continue
        if parsed.scheme or parsed.netloc or not parsed.path:
            continue

        target_path = Path(unquote(parsed.path))
        if target_path.is_absolute():
            destination = (root / str(target_path).lstrip("/\\")).resolve()
        else:
            destination = (source_path.parent / target_path).resolve()

        try:
            destination.relative_to(root.resolve())
        except ValueError:
            errors.append(
                f"{relative_path}: ссылка выходит за пределы репозитория: {target}."
            )
            continue
        if not destination.exists():
            errors.append(f"{relative_path}: внутренняя ссылка не существует: {target}.")

    for match in SKILL_REFERENCE.finditer(text):
        skill_name = match.group(1)
        skill_references.add(skill_name)
        skill_path = root / ".agents" / "skills" / skill_name / "SKILL.md"
        if not skill_path.is_file():
            errors.append(
                f"{relative_path}: ссылка на project-owned skill не существует: "
                f".agents/skills/{skill_name}/SKILL.md."
            )
    return skill_references


def validate_repository(repository_root: Path) -> list[str]:
    root = repository_root.resolve()
    errors: list[str] = []
    skill_paths: dict[str, Path] = {}
    skill_names: dict[str, str] = {}

    for skill_name in sorted(PROJECT_OWNED_SKILLS):
        skill_path = root / ".agents" / "skills" / skill_name / "SKILL.md"
        relative_path = skill_path.relative_to(root).as_posix()
        skill_paths[skill_name] = skill_path
        text = read_text(skill_path, root, errors)
        if text is None:
            continue

        metadata = parse_frontmatter(text, relative_path, errors)
        for required_key in ("name", "description"):
            if required_key not in metadata:
                errors.append(f"{relative_path}: отсутствует '{required_key}'.")
            elif not is_nonempty_frontmatter_value(metadata[required_key]):
                errors.append(f"{relative_path}: '{required_key}' не должен быть пустым.")

        skill_name_value = metadata.get("name", "")
        if skill_name_value:
            if skill_name_value != skill_name:
                errors.append(
                    f"{relative_path}: name '{skill_name_value}' не совпадает "
                    f"с именем каталога '{skill_name}'."
                )
            previous_path = skill_names.get(skill_name_value)
            if previous_path is not None:
                errors.append(
                    f"{relative_path}: skill name '{skill_name_value}' уже "
                    f"используется в {previous_path}."
                )
            else:
                skill_names[skill_name_value] = relative_path

        validate_markdown_references(root, skill_path, text, errors)

    instructions_directory = root / ".github" / "instructions"
    instruction_paths = sorted(instructions_directory.glob("*.instructions.md"))
    if not instruction_paths:
        errors.append(".github/instructions: path-specific instructions не найдены.")

    for instruction_path in instruction_paths:
        relative_path = instruction_path.relative_to(root).as_posix()
        text = read_text(instruction_path, root, errors)
        if text is None:
            continue
        metadata = parse_frontmatter(text, relative_path, errors)
        if "applyTo" not in metadata:
            errors.append(f"{relative_path}: отсутствует 'applyTo'.")
        elif not is_nonempty_frontmatter_value(metadata["applyTo"]):
            errors.append(f"{relative_path}: 'applyTo' не должен быть пустым.")
        validate_markdown_references(root, instruction_path, text, errors)

    control_paths = [
        root / "AGENTS.md",
        root / ".github" / "copilot-instructions.md",
    ]
    for control_path in control_paths:
        text = read_text(control_path, root, errors)
        if text is not None:
            referenced_skills = validate_markdown_references(
                root,
                control_path,
                text,
                errors,
            )
            for skill_name in sorted(
                REQUIRED_ROUTING_REFERENCES - referenced_skills
            ):
                errors.append(
                    f"{control_path.relative_to(root).as_posix()}: "
                    f"обязательная routing-ссылка на "
                    f".agents/skills/{skill_name}/SKILL.md отсутствует."
                )

    registry_path = root / ".agents" / "skills" / "README.md"
    registry_text = read_text(registry_path, root, errors)
    if registry_text is not None:
        registry_names = project_owned_registry_names(registry_text)
        if not registry_names:
            errors.append(
                ".agents/skills/README.md: отсутствует реестр Project-owned skills."
            )
        duplicates = sorted(
            name for name in set(registry_names) if registry_names.count(name) > 1
        )
        for name in duplicates:
            errors.append(
                f".agents/skills/README.md: project-owned skill '{name}' указан повторно."
            )
        listed_names = set(registry_names)
        missing_names = PROJECT_OWNED_SKILLS - listed_names
        extra_names = listed_names - PROJECT_OWNED_SKILLS
        for name in sorted(missing_names):
            errors.append(
                f".agents/skills/README.md: обязательный skill '{name}' не указан."
            )
        for name in sorted(extra_names):
            errors.append(
                f".agents/skills/README.md: неизвестный project-owned skill "
                f"'{name}'."
            )
        validate_markdown_references(root, registry_path, registry_text, errors)

    return errors


def main() -> int:
    parser = argparse.ArgumentParser(
        description="Проверяет repository-owned agent development assets."
    )
    parser.add_argument(
        "repository_root",
        nargs="?",
        type=Path,
        default=Path("."),
    )
    args = parser.parse_args()

    errors = validate_repository(args.repository_root)
    if errors:
        print("Проверка agent assets не пройдена:", file=sys.stderr)
        for error in errors:
            print(f"- {error}", file=sys.stderr)
        return 1

    print("Проверка agent assets пройдена.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
