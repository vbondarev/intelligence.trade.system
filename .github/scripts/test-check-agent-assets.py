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
WORKFLOW_PROJECT_OWNED_SKILLS = validator["WORKFLOW_PROJECT_OWNED_SKILLS"]
SPECIALIZED_PROJECT_OWNED_SKILLS = validator["SPECIALIZED_PROJECT_OWNED_SKILLS"]

SPECIALIZED_SKILL = "trade-system-web-design-review"
FRONTEND_AGENTS = "frontend/intelligence-trade-web/AGENTS.md"
SPECIALIZED_ROUTE = f".agents/skills/{SPECIALIZED_SKILL}/SKILL.md"


def write_text(root: Path, relative_path: str, text: str) -> Path:
    path = root / relative_path
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(text, encoding="utf-8")
    return path


def registry_table(skill_names: set[str]) -> list[str]:
    return [
        "| Skill | Responsibility |",
        "| --- | --- |",
        *(f"| `{skill_name}` | Процесс |" for skill_name in sorted(skill_names)),
    ]


def create_repository(root: Path) -> None:
    write_text(root, "docs/README.md", "# Документация\n")
    workflow_references = "\n".join(
        f".agents/skills/{skill_name}/SKILL.md"
        for skill_name in sorted(WORKFLOW_PROJECT_OWNED_SKILLS)
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
        FRONTEND_AGENTS,
        "# Frontend\n\n[Документация](../../docs/README.md)\n\n"
        f"UI review — `{SPECIALIZED_ROUTE}`.\n",
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
                "### Workflow skills",
                "",
                *registry_table(WORKFLOW_PROJECT_OWNED_SKILLS),
                "",
                "### Specialized skills",
                "",
                *registry_table(SPECIALIZED_PROJECT_OWNED_SKILLS),
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
    for skill_name in WORKFLOW_PROJECT_OWNED_SKILLS:
        write_text(
            root,
            f".agents/skills/{skill_name}/SKILL.md",
            f"---\nname: {skill_name}\ndescription: Описание workflow.\n---\n"
            "# Процесс\n",
        )
    for skill_name in SPECIALIZED_PROJECT_OWNED_SKILLS:
        write_text(
            root,
            f".agents/skills/{skill_name}/SKILL.md",
            f"---\nname: {skill_name}\ndescription: Описание review.\n---\n"
            "# Review\n\n[Guidelines](guidelines.md)\n",
        )
        write_text(root, f".agents/skills/{skill_name}/guidelines.md", "# Rules\n")


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

    def test_visible_markdown_link_passes(self) -> None:
        self.assertEqual([], self.errors())

    def test_hidden_markdown_links_are_ignored(self) -> None:
        path = self.root / "AGENTS.md"
        path.write_text(
            path.read_text(encoding="utf-8")
            + "\n<!--\n[Скрытая ссылка](missing-hidden.md)\n-->\n"
            "```text\n[Ссылка в коде](missing-fenced.md)\n```\n"
            "    [Ссылка в indented code](missing-indented.md)\n",
            encoding="utf-8",
        )
        self.assertEqual([], self.errors())

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

    def test_control_file_route_inside_html_comment_fails(self) -> None:
        path = self.root / "AGENTS.md"
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                route,
                f"<!--\n{route}\n-->",
                1,
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_control_file_route_inside_fenced_code_fails(self) -> None:
        path = self.root / "AGENTS.md"
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                route,
                f"```text\n{route}\n```",
                1,
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_control_file_route_inside_indented_code_fails(self) -> None:
        path = self.root / "AGENTS.md"
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                route,
                f"    {route}",
                1,
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_tab_indented_control_file_route_fails(self) -> None:
        path = self.root / "AGENTS.md"
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        path.write_text(
            path.read_text(encoding="utf-8").replace(
                route,
                f"\t{route}",
                1,
            ),
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_shorter_fence_does_not_close_longer_fence(self) -> None:
        path = self.root / "AGENTS.md"
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        content = path.read_text(encoding="utf-8").replace(route, "", 1)
        path.write_text(
            content
            + "\n````text\n"
            + ".agents/skills/trade-system-pr-review/SKILL.md\n"
            + "```\n"
            + route
            + "\n",
            encoding="utf-8",
        )
        self.assertTrue(
            any(
                "обязательная routing-ссылка" in error and "trade-system-delivery" in error
                for error in self.errors()
            )
        )

    def test_matching_or_longer_fence_closes_longer_fence(self) -> None:
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        for closing_fence in ("````", "`````"):
            with self.subTest(closing_fence=closing_fence):
                path = self.root / "AGENTS.md"
                content = path.read_text(encoding="utf-8").replace(route, "", 1)
                path.write_text(
                    content
                    + "\n````text\n"
                    + ".agents/skills/trade-system-pr-review/SKILL.md\n"
                    + closing_fence
                    + "\n"
                    + route
                    + "\n",
                    encoding="utf-8",
                )
                self.assertEqual([], self.errors())

    def test_tilde_fence_closes_with_same_character_and_length(self) -> None:
        route = ".agents/skills/trade-system-delivery/SKILL.md"
        path = self.root / "AGENTS.md"
        content = path.read_text(encoding="utf-8").replace(route, "", 1)
        path.write_text(
            content
            + "\n~~~~text\n"
            + ".agents/skills/trade-system-pr-review/SKILL.md\n"
            + "~~~~~\n"
            + route
            + "\n",
            encoding="utf-8",
        )
        self.assertEqual([], self.errors())

    def test_control_file_missing_all_required_routes_fails(self) -> None:
        path = self.root / ".github" / "copilot-instructions.md"
        path.write_text("# Routing удалён\n", encoding="utf-8")
        errors = self.errors()
        missing_routes = [
            error for error in errors if "обязательная routing-ссылка" in error
        ]
        self.assertEqual(len(WORKFLOW_PROJECT_OWNED_SKILLS), len(missing_routes))
        self.assertFalse(any(SPECIALIZED_SKILL in error for error in missing_routes))

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

    def _registry_with_delivery_row(self, hidden_row: str) -> None:
        path = self.root / ".agents" / "skills" / "README.md"
        text = path.read_text(encoding="utf-8")
        row = "| `trade-system-delivery` | Процесс |"
        path.write_text(
            text.replace(row + "\n", "", 1) + "\n" + hidden_row + "\n",
            encoding="utf-8",
        )

    def test_registry_row_inside_html_comment_is_ignored(self) -> None:
        self._registry_with_delivery_row(
            "<!--\n| `trade-system-delivery` | Процесс |\n-->"
        )
        self.assertTrue(
            any(
                "обязательный skill 'trade-system-delivery' не указан" in error
                for error in self.errors()
            )
        )

    def test_registry_row_inside_fenced_code_is_ignored(self) -> None:
        self._registry_with_delivery_row(
            "```\n| `trade-system-delivery` | Процесс |\n```"
        )
        self.assertTrue(
            any(
                "обязательный skill 'trade-system-delivery' не указан" in error
                for error in self.errors()
            )
        )

    def test_registry_row_inside_indented_code_is_ignored(self) -> None:
        self._registry_with_delivery_row(
            "    | `trade-system-delivery` | Процесс |"
        )
        self.assertTrue(
            any(
                "обязательный skill 'trade-system-delivery' не указан" in error
                for error in self.errors()
            )
        )

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


class SkillCategoriesTests(unittest.TestCase):
    def setUp(self) -> None:
        self.temporary_directory = TemporaryDirectory()
        self.addCleanup(self.temporary_directory.cleanup)
        self.root = Path(self.temporary_directory.name)
        create_repository(self.root)
        self.registry = self.root / ".agents" / "skills" / "README.md"

    def errors(self) -> list[str]:
        return validate_repository(self.root)

    def assert_error(self, *fragments: str) -> None:
        errors = self.errors()
        self.assertTrue(
            any(all(fragment in error for fragment in fragments) for error in errors),
            errors,
        )

    def replace_in(self, path: Path, old: str, new: str) -> None:
        text = path.read_text(encoding="utf-8")
        self.assertIn(old, text)
        path.write_text(text.replace(old, new, 1), encoding="utf-8")

    def row(self, skill_name: str) -> str:
        return f"| `{skill_name}` | Процесс |\n"

    def test_categories_are_disjoint_and_complete(self) -> None:
        self.assertEqual(
            set(),
            WORKFLOW_PROJECT_OWNED_SKILLS & SPECIALIZED_PROJECT_OWNED_SKILLS,
        )
        self.assertEqual(
            PROJECT_OWNED_SKILLS,
            WORKFLOW_PROJECT_OWNED_SKILLS | SPECIALIZED_PROJECT_OWNED_SKILLS,
        )
        self.assertIn(SPECIALIZED_SKILL, SPECIALIZED_PROJECT_OWNED_SKILLS)

    def test_valid_workflow_and_specialized_skills_pass(self) -> None:
        self.assertEqual([], self.errors())

    def test_missing_workflow_skill_file_fails(self) -> None:
        (
            self.root / ".agents" / "skills" / "trade-system-pr-review" / "SKILL.md"
        ).unlink()
        self.assert_error("trade-system-pr-review/SKILL.md", "не удалось прочитать файл")

    def test_missing_specialized_skill_file_fails(self) -> None:
        (self.root / ".agents" / "skills" / SPECIALIZED_SKILL / "SKILL.md").unlink()
        self.assert_error(f"{SPECIALIZED_SKILL}/SKILL.md", "не удалось прочитать файл")

    def test_specialized_skill_name_must_match_directory(self) -> None:
        self.replace_in(
            self.root / ".agents" / "skills" / SPECIALIZED_SKILL / "SKILL.md",
            f"name: {SPECIALIZED_SKILL}",
            "name: web-design-guidelines",
        )
        self.assert_error(SPECIALIZED_SKILL, "не совпадает с именем каталога")

    def test_specialized_skill_requires_description(self) -> None:
        self.replace_in(
            self.root / ".agents" / "skills" / SPECIALIZED_SKILL / "SKILL.md",
            "description: Описание review.\n",
            "",
        )
        self.assert_error(SPECIALIZED_SKILL, "отсутствует 'description'")

    def test_specialized_skill_is_not_required_in_global_routing(self) -> None:
        for relative_path in ("AGENTS.md", ".github/copilot-instructions.md"):
            text = (self.root / relative_path).read_text(encoding="utf-8")
            self.assertNotIn(SPECIALIZED_ROUTE, text)
        self.assertEqual([], self.errors())

    def test_missing_frontend_context_route_fails(self) -> None:
        self.replace_in(self.root / FRONTEND_AGENTS, SPECIALIZED_ROUTE, "UI review skill")
        self.assert_error(
            FRONTEND_AGENTS,
            "обязательная context routing-ссылка",
            SPECIALIZED_SKILL,
        )

    def test_frontend_context_route_inside_fenced_code_fails(self) -> None:
        self.replace_in(
            self.root / FRONTEND_AGENTS,
            f"`{SPECIALIZED_ROUTE}`",
            f"\n```text\n{SPECIALIZED_ROUTE}\n```\n",
        )
        self.assert_error(FRONTEND_AGENTS, "обязательная context routing-ссылка")

    def test_missing_frontend_agents_fails(self) -> None:
        (self.root / FRONTEND_AGENTS).unlink()
        self.assert_error(FRONTEND_AGENTS, "не удалось прочитать файл")

    def test_broken_link_in_frontend_agents_fails(self) -> None:
        self.replace_in(
            self.root / FRONTEND_AGENTS,
            "../../docs/README.md",
            "../../docs/missing.md",
        )
        self.assert_error(FRONTEND_AGENTS, "внутренняя ссылка не существует")

    def test_workflow_skill_in_specialized_registry_fails(self) -> None:
        row = self.row("trade-system-delivery")
        self.replace_in(self.registry, row, "")
        self.replace_in(self.registry, "\n## External skills", row + "\n## External skills")
        self.assert_error(
            "'trade-system-delivery'",
            "указан в разделе 'Specialized skills'",
            "относится к разделу 'Workflow skills'",
        )

    def test_specialized_skill_in_workflow_registry_fails(self) -> None:
        row = self.row(SPECIALIZED_SKILL)
        self.replace_in(self.registry, row, "")
        self.replace_in(self.registry, "\n### Specialized skills", row + "\n### Specialized skills")
        self.assert_error(
            f"'{SPECIALIZED_SKILL}'",
            "указан в разделе 'Workflow skills'",
            "относится к разделу 'Specialized skills'",
        )

    def test_unknown_specialized_entry_fails(self) -> None:
        self.replace_in(
            self.registry,
            "\n## External skills",
            self.row("trade-system-unknown-review") + "\n## External skills",
        )
        self.assert_error(
            "неизвестный project-owned skill 'trade-system-unknown-review'",
            "'Specialized skills'",
        )

    def test_missing_specialized_registry_entry_fails(self) -> None:
        self.replace_in(self.registry, self.row(SPECIALIZED_SKILL), "")
        self.assert_error(
            f"обязательный skill '{SPECIALIZED_SKILL}' не указан",
            "'Specialized skills'",
        )

    def test_skill_in_both_categories_fails(self) -> None:
        self.replace_in(
            self.registry,
            "\n### Specialized skills",
            self.row(SPECIALIZED_SKILL) + "\n### Specialized skills",
        )
        self.assert_error(f"'{SPECIALIZED_SKILL}'", "указан одновременно")

    def test_duplicate_entry_within_category_fails(self) -> None:
        row = self.row(SPECIALIZED_SKILL)
        self.replace_in(self.registry, row, row + row)
        self.assert_error(f"'{SPECIALIZED_SKILL}'", "указан повторно")

    def test_missing_category_heading_fails(self) -> None:
        self.replace_in(self.registry, "### Specialized skills\n", "")
        self.assert_error("отсутствует раздел '### Specialized skills'")

    def test_entry_outside_categories_fails(self) -> None:
        self.replace_in(
            self.registry,
            "### Workflow skills\n",
            self.row("trade-system-delivery") + "\n### Workflow skills\n",
        )
        self.assert_error("'trade-system-delivery'", "указан вне разделов")

    def test_external_skill_stays_outside_project_owned_validation(self) -> None:
        write_text(
            self.root,
            ".agents/skills/external-react/SKILL.md",
            "---\nname: vendor-react\ndescription:\n  multi-line upstream value\n"
            "---\n[Upstream link](missing-upstream.md)\n",
        )
        self.replace_in(
            self.root / FRONTEND_AGENTS,
            "UI review",
            "React — `.agents/skills/external-react/SKILL.md`; UI review",
        )
        self.assertEqual([], self.errors())

    def test_broken_specialized_guidelines_link_fails(self) -> None:
        (self.root / ".agents" / "skills" / SPECIALIZED_SKILL / "guidelines.md").unlink()
        self.assert_error(f"{SPECIALIZED_SKILL}/SKILL.md", "внутренняя ссылка не существует")


if __name__ == "__main__":
    unittest.main()
