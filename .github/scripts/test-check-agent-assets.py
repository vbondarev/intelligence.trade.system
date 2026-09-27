#!/usr/bin/env python3
"""Самодостаточные regression tests validator agent assets."""

from __future__ import annotations

from pathlib import Path
import runpy
from tempfile import TemporaryDirectory
import unittest


validator = runpy.run_path(
    str(Path(__file__).with_name("check-agent-assets.py"))
)
validate_repository = validator["validate_repository"]
PROJECT_OWNED_SKILLS = validator["PROJECT_OWNED_SKILLS"]


def write_text(root: Path, relative_path: str, text: str) -> Path:
    path = root / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


def create_repository(root: Path) -> None:
    write_text(root, "docs/README.md", "# Документация\n")
    workflow_references = "\n".join(
        f".agents/skills/{skill_name}/SKILL.md"
        for skill_name in sorted(PROJECT_OWNED_SKILLS)
    )
    write_text(
        root,
        "AGENTS.md",
        "# Правила\n\n[Документация](docs/README.md)\n\n"
        f"{workflow_references}\n",
    )
    write_text(
        root,
        ".github/copilot-instructions.md",
        workflow_references + "\n",
    )
    write_text(
        root,
        ".agents/skills/README.md",
        "\n".join(
            [
                "# Skills",
                "",
                "## Project-owned skills",
                "",
                "| Skill | Responsibility |",
                "| --- | --- |",
                *(
                    f"| `{skill_name}` | Процесс |"
                    for skill_name in sorted(PROJECT_OWNED_SKILLS)
                ),
                "",
                "## External skills",
                "",
            ]
        ),
    )
    write_text(
        root,
        ".github/instructions/example.instructions.md",
        "---\napplyTo: \"src/**\"\n---\n# Инструкции\n",
    )
    for skill_name in PROJECT_OWNED_SKILLS:
        write_text(
            root,
            f".agents/skills/{skill_name}/SKILL.md",
            f"---\nname: {skill_name}\ndescription: Описание workflow.\n---\n"
            "# Процесс\n",
        )


class AgentAssetsValidatorTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        create_repository(self.root)

    def errors(self) -> list[str]:
        return validate_repository(self.root)

    def skill_file(self, skill_name: str = "trade-system-delivery") -> Path:
        return self.root / ".agents" / "skills" / skill_name / "SKILL.md"

    def test_valid_project_owned_assets_pass(self) -> None:
        self.assertEqual([], self.errors())

    def test_missing_skill_name_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace("name: trade-system-delivery\n", ""),
            encoding="utf-8",
        )
        self.assertTrue(any("отсутствует 'name'" in error for error in self.errors()))

    def test_empty_skill_name_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "name: trade-system-delivery",
                'name: ""',
            ),
            encoding="utf-8",
        )
        self.assertTrue(any("'name' не должен быть пустым" in error for error in self.errors()))

    def test_malformed_quoted_frontmatter_value_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "description: Описание workflow.",
                'description: "unterminated',
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any("некорректное quoted значение frontmatter" in error for error in self.errors())
        )

    def test_malformed_unquoted_frontmatter_values_fail(self) -> None:
        path = self.skill_file()
        original = path.read_text(encoding="utf-8")
        for invalid_value in (
            "[unterminated",
            "{key: value",
            "value: invalid",
            "true",
        ):
            with self.subTest(invalid_value=invalid_value):
                path.write_text(
                    original.replace(
                        "description: Описание workflow.",
                        f"description: {invalid_value}",
                    ),
                    encoding="utf-8",
                )
                self.assertTrue(
                    any(
                        "некорректный unquoted scalar" in error
                        for error in self.errors()
                    )
                )

    def test_indented_frontmatter_mapping_entry_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            "---\n"
            "name: trade-system-delivery\n"
            "description: Описание workflow.\n"
            "  unexpected: value\n"
            "---\n"
            "# Процесс\n",
            encoding="utf-8",
        )
        self.assertTrue(
            any("вложенные записи frontmatter не поддерживаются" in error for error in self.errors())
        )

    def test_indented_frontmatter_scalar_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            "---\n"
            "name: trade-system-delivery\n"
            "  malformed\n"
            "description: Описание workflow.\n"
            "---\n"
            "# Процесс\n",
            encoding="utf-8",
        )
        self.assertTrue(
            any("вложенные записи frontmatter не поддерживаются" in error for error in self.errors())
        )

    def test_blank_frontmatter_line_is_allowed(self) -> None:
        path = self.skill_file()
        path.write_text(
            "---\n"
            "name: trade-system-delivery\n"
            "\n"
            "description: Описание workflow.\n"
            "---\n"
            "# Процесс\n",
            encoding="utf-8",
        )
        self.assertEqual([], self.errors())

    def test_valid_quoted_frontmatter_scalars_pass(self) -> None:
        path = self.skill_file()
        path.write_text(
            '---\nname: "trade-system-delivery"\n'
            'description: "Описание [workflow]"\n---\n# Процесс\n',
            encoding="utf-8",
        )
        self.assertEqual([], self.errors())

    def test_missing_skill_description_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "description: Описание workflow.\n",
                "",
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any("отсутствует 'description'" in error for error in self.errors())
        )

    def test_duplicate_skill_name_fails(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "---\n# Процесс",
                "name: other-name\n---\n# Процесс",
            ),
            encoding="utf-8",
        )
        self.assertTrue(any("повторяется ключ 'name'" in error for error in self.errors()))

    def test_same_skill_name_in_multiple_directories_fails(self) -> None:
        path = self.skill_file("trade-system-discovery")
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "name: trade-system-discovery",
                "name: trade-system-delivery",
            ),
            encoding="utf-8",
        )
        self.assertTrue(any("уже используется" in error for error in self.errors()))

    def test_skill_name_must_match_directory(self) -> None:
        path = self.skill_file()
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "name: trade-system-delivery",
                "name: different-skill",
            ),
            encoding="utf-8",
        )
        self.assertTrue(any("не совпадает с именем каталога" in error for error in self.errors()))

    def test_missing_required_workflow_skill_fails(self) -> None:
        self.skill_file("trade-system-discovery").unlink()
        self.assertTrue(any("не удалось прочитать файл" in error for error in self.errors()))

    def test_broken_markdown_link_fails(self) -> None:
        path = self.root / "AGENTS.md"
        path.write_text(
            path.read_text(encoding="utf-8") + "\n[Broken](missing.md)\n",
            encoding="utf-8",
        )
        self.assertTrue(any("внутренняя ссылка не существует" in error for error in self.errors()))

    def test_broken_skill_routing_reference_fails(self) -> None:
        path = self.root / ".github" / "copilot-instructions.md"
        path.write_text(
            path.read_text(encoding="utf-8")
            + ".agents/skills/missing-skill/SKILL.md\n",
            encoding="utf-8",
        )
        self.assertTrue(
            any("ссылка на project-owned skill не существует" in error for error in self.errors())
        )

    def test_control_file_missing_one_required_route_fails(self) -> None:
        path = self.root / "AGENTS.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                ".agents/skills/trade-system-delivery/SKILL.md\n",
                "",
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error
                and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_control_file_missing_all_required_routes_fails(self) -> None:
        path = self.root / ".github" / "copilot-instructions.md"
        path.write_text("# Routing удалён\n", encoding="utf-8")
        errors = self.errors()
        missing_routes = [
            error for error in errors if "обязательная routing-ссылка" in error
        ]
        self.assertEqual(len(PROJECT_OWNED_SKILLS), len(missing_routes))

    def test_instruction_without_apply_to_fails(self) -> None:
        path = self.root / ".github" / "instructions" / "example.instructions.md"
        path.write_text("---\n---\n# Инструкции\n", encoding="utf-8")
        self.assertTrue(any("отсутствует 'applyTo'" in error for error in self.errors()))

    def test_instruction_with_empty_apply_to_fails(self) -> None:
        path = self.root / ".github" / "instructions" / "example.instructions.md"
        path.write_text('---\napplyTo: ""\n---\n# Инструкции\n', encoding="utf-8")
        self.assertTrue(
            any("'applyTo' не должен быть пустым" in error for error in self.errors())
        )

    def test_unclosed_skill_frontmatter_fails(self) -> None:
        self.skill_file().write_text(
            "---\nname: trade-system-delivery\ndescription: Missing close\n",
            encoding="utf-8",
        )
        self.assertTrue(any("frontmatter не закрыт" in error for error in self.errors()))

    def test_registry_unknown_skill_fails(self) -> None:
        path = self.root / ".agents" / "skills" / "README.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                "## External skills",
                "| `missing-skill` | Процесс |\n\n## External skills",
            ),
            encoding="utf-8",
        )
        self.assertTrue(any("неизвестный project-owned skill" in error for error in self.errors()))

    def test_registry_missing_required_skill_fails(self) -> None:
        path = self.root / ".agents" / "skills" / "README.md"
        path.write_text(
            "\n".join(
                line
                for line in path.read_text(encoding="utf-8").splitlines()
                if "`trade-system-discovery`" not in line
            )
            + "\n",
            encoding="utf-8",
        )
        self.assertTrue(any("не указан" in error for error in self.errors()))

    def test_openclaw_is_excluded(self) -> None:
        write_text(
            self.root,
            "openclaw/AGENTS.md",
            "[Битая](missing.md)\n",
        )
        write_text(
            self.root,
            "openclaw/skills/example/SKILL.md",
            "not valid project skill metadata",
        )
        self.assertEqual([], self.errors())

    def test_external_skill_frontmatter_is_not_project_validated(self) -> None:
        write_text(
            self.root,
            ".agents/skills/external-upstream/SKILL.md",
            "upstream content without project frontmatter rules",
        )
        self.assertEqual([], self.errors())


if __name__ == "__main__":
    unittest.main()
