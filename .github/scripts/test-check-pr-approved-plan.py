#!/usr/bin/env python3
"""Проверяет регрессии current-state Approved Plan validator."""

from __future__ import annotations

from pathlib import Path
import runpy
from unittest import TestCase, main
from unittest.mock import patch


validator = runpy.run_path(
    str(Path(__file__).with_name("check-pr-approved-plan.py"))
)
AMENDMENT_MARKER = validator["AMENDMENT_MARKER"]
BASE_PLAN_MARKER = validator["BASE_PLAN_MARKER"]
TRUSTED_PLAN_AUTHOR = validator["TRUSTED_PLAN_AUTHOR"]
ValidationError = validator["ValidationError"]
request_json = validator["request_json"]
validate_current_pull_request = validator["validate_current_pull_request"]
validate_pull_request = validator["validate_pull_request"]

REPOSITORY = "vbondarev/intelligence.trade.system"
ISSUE_NUMBER = 165
BASE_COMMENT_ID = 5851955523
BASE_PERMALINK = (
    f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
    f"#issuecomment-{BASE_COMMENT_ID}"
)
CREATED_AT = "2026-09-27T02:26:21Z"


def issue_permalink(comment_id: int) -> str:
    return (
        f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
        f"#issuecomment-{comment_id}"
    )


def make_comment(
    comment_id: int,
    body: str,
    *,
    author: str = TRUSTED_PLAN_AUTHOR,
    association: str = "OWNER",
    created_at: str = CREATED_AT,
    updated_at: str | None = None,
) -> dict[str, object]:
    return {
        "id": comment_id,
        "body": body,
        "html_url": issue_permalink(comment_id),
        "author_association": association,
        "user": {"login": author},
        "created_at": created_at,
        "updated_at": updated_at or created_at,
    }


def make_base_plan(
    comment_id: int = BASE_COMMENT_ID,
    *,
    marker: str = BASE_PLAN_MARKER,
    **comment_values: str,
) -> dict[str, object]:
    return make_comment(comment_id, f"{marker}\n\nPlan.\n", **comment_values)


def make_amendment(
    comment_id: int,
    base_permalink: str = BASE_PERMALINK,
    **comment_values: str,
) -> dict[str, object]:
    return make_comment(
        comment_id,
        f"{AMENDMENT_MARKER}\n\n"
        f"Base Approved Implementation Plan: {base_permalink}\n\n"
        "Утверждённое изменение.\n",
        **comment_values,
    )


def make_issue(*, is_pull_request: bool = False) -> dict[str, object]:
    issue: dict[str, object] = {"number": ISSUE_NUMBER, "title": "Tech-G09"}
    if is_pull_request:
        issue["pull_request"] = {
            "url": f"https://api.github.com/repos/{REPOSITORY}/pulls/164"
        }
    return issue


def make_body(
    base_permalink: str = BASE_PERMALINK,
    amendment_permalinks: list[str] | None = None,
) -> str:
    listed = ["Нет"] if amendment_permalinks is None else amendment_permalinks
    return (
        f"Closes #{ISSUE_NUMBER}\n\n"
        f"Approved Implementation Plan: {base_permalink}\n\n"
        "Approved Plan Amendments:\n"
        + "\n".join(f"- {value}" for value in listed)
        + "\n"
    )


class PullRequestPlanValidatorTests(TestCase):
    def assert_invalid(
        self,
        body: str,
        comments: list[dict[str, object]],
        issue: dict[str, object] | None = None,
    ) -> None:
        with self.assertRaises(ValidationError):
            validate_pull_request(
                body,
                REPOSITORY,
                make_issue() if issue is None else issue,
                comments,
            )

    def test_real_pr_template_is_compatible_with_amendment_parser(self) -> None:
        template_path = Path(__file__).resolve().parents[1] / "pull_request_template.md"
        body = template_path.read_text(encoding="utf-8")
        body = body.replace("Closes #", f"Closes #{ISSUE_NUMBER}", 1)
        body = body.replace(
            "Approved Implementation Plan: <permalink или N/A>",
            f"Approved Implementation Plan: {BASE_PERMALINK}",
            1,
        )

        result = validate_pull_request(
            body,
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )

        self.assertIn("0 amendment(s)", result)

    def test_html_comment_inside_amendments_section_is_ignored(self) -> None:
        body = make_body().replace(
            "Approved Plan Amendments:\n- Нет\n",
            "Approved Plan Amendments:\n- Нет\n<!-- Инструкция внутри секции. -->\n",
        )

        result = validate_pull_request(
            body,
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )

        self.assertIn("0 amendment(s)", result)

    def test_valid_trusted_immutable_base_plan(self) -> None:
        result = validate_pull_request(
            make_body(),
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )
        self.assertIn("0 amendment(s)", result)

    def test_valid_base_with_one_amendment(self) -> None:
        amendment = make_amendment(BASE_COMMENT_ID + 1)
        result = validate_pull_request(
            make_body(amendment_permalinks=[issue_permalink(BASE_COMMENT_ID + 1)]),
            REPOSITORY,
            make_issue(),
            [make_base_plan(), amendment],
        )
        self.assertIn("1 amendment(s)", result)

    def test_valid_base_with_multiple_amendments(self) -> None:
        ids = [BASE_COMMENT_ID + 1, BASE_COMMENT_ID + 2]
        result = validate_pull_request(
            make_body(amendment_permalinks=[issue_permalink(value) for value in ids]),
            REPOSITORY,
            make_issue(),
            [make_base_plan(), *(make_amendment(value) for value in ids)],
        )
        self.assertIn("2 amendment(s)", result)

    def test_amendments_are_required_in_issue_comment_order(self) -> None:
        first_id, second_id = BASE_COMMENT_ID + 1, BASE_COMMENT_ID + 2
        self.assert_invalid(
            make_body(
                amendment_permalinks=[
                    issue_permalink(second_id),
                    issue_permalink(first_id),
                ]
            ),
            [
                make_base_plan(),
                make_amendment(first_id),
                make_amendment(second_id),
            ],
        )

    def test_leading_blank_lines_before_closes_succeed(self) -> None:
        result = validate_pull_request(
            "\n\n" + make_body(),
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )

        self.assertIn("0 amendment(s)", result)

    def test_closes_inside_html_comment_fails(self) -> None:
        body = make_body().replace(
            f"Closes #{ISSUE_NUMBER}",
            f"<!--\nCloses #{ISSUE_NUMBER}\n-->",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_closes_inside_fenced_code_fails(self) -> None:
        body = make_body().replace(
            f"Closes #{ISSUE_NUMBER}",
            f"```text\nCloses #{ISSUE_NUMBER}\n```",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_indented_closes_fails(self) -> None:
        body = make_body().replace(
            f"Closes #{ISSUE_NUMBER}",
            f"    Closes #{ISSUE_NUMBER}",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_hidden_additional_closes_do_not_count(self) -> None:
        body = make_body().replace(
            f"Closes #{ISSUE_NUMBER}\n\n",
            (
                f"Closes #{ISSUE_NUMBER}\n\n"
                "<!--\nCloses #164\n-->\n\n"
                "```text\nCloses #163\n```\n\n"
            ),
            1,
        )

        result = validate_pull_request(
            body,
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )

        self.assertIn("0 amendment(s)", result)

    def test_content_before_primary_closes_fails(self) -> None:
        self.assert_invalid(
            "## Связанная задача\n\n" + make_body(),
            [make_base_plan()],
        )

    def test_missing_closes_fails(self) -> None:
        self.assert_invalid(
            make_body().replace(f"Closes #{ISSUE_NUMBER}\n", ""),
            [make_base_plan()],
        )

    def test_multiple_primary_closes_fail(self) -> None:
        self.assert_invalid(
            make_body().replace(
                f"Closes #{ISSUE_NUMBER}",
                f"Closes #{ISSUE_NUMBER}\nCloses #164",
            ),
            [make_base_plan()],
        )

    def test_closes_to_pull_request_fails(self) -> None:
        self.assert_invalid(make_body(), [make_base_plan()], make_issue(is_pull_request=True))

    def test_hidden_plan_field_fails(self) -> None:
        body = make_body().replace(
            f"Approved Implementation Plan: {BASE_PERMALINK}",
            (
                "<!--\n"
                f"Approved Implementation Plan: {BASE_PERMALINK}\n"
                "-->"
            ),
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_indented_plan_field_fails(self) -> None:
        body = make_body().replace(
            f"Approved Implementation Plan: {BASE_PERMALINK}",
            f"    Approved Implementation Plan: {BASE_PERMALINK}",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_hidden_amendments_section_fails(self) -> None:
        body = make_body().replace(
            "Approved Plan Amendments:\n- Нет",
            "<!--\nApproved Plan Amendments:\n- Нет\n-->",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_indented_amendments_section_fails(self) -> None:
        body = make_body().replace(
            "Approved Plan Amendments:",
            "    Approved Plan Amendments:",
            1,
        )

        self.assert_invalid(body, [make_base_plan()])

    def test_hidden_duplicate_metadata_is_ignored(self) -> None:
        body = (
            make_body()
            + "\n<!--\n"
            + "Approved Implementation Plan: N/A\n"
            + "Approved Plan Amendments:\n- Нет\n"
            + "-->\n"
        )

        result = validate_pull_request(
            body,
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )

        self.assertIn("0 amendment(s)", result)

    def test_missing_plan_field_fails(self) -> None:
        self.assert_invalid(
            make_body().replace(f"Approved Implementation Plan: {BASE_PERMALINK}\n", ""),
            [make_base_plan()],
        )

    def test_malformed_plan_permalink_fails(self) -> None:
        self.assert_invalid(make_body("not-a-permalink"), [make_base_plan()])

    def test_other_repository_permalink_fails(self) -> None:
        self.assert_invalid(
            make_body(BASE_PERMALINK.replace(REPOSITORY, "other/repo")),
            [make_base_plan()],
        )

    def test_other_issue_permalink_fails(self) -> None:
        self.assert_invalid(
            make_body(BASE_PERMALINK.replace("/165#", "/164#")),
            [make_base_plan()],
        )

    def test_missing_linked_comment_fails(self) -> None:
        self.assert_invalid(make_body(), [])

    def test_wrong_base_marker_fails(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(marker="# Not an Approved Implementation Plan")],
        )

    def test_base_by_untrusted_author_is_not_trusted(self) -> None:
        self.assert_invalid(
            make_body(),
            [
                make_base_plan(
                    author="untrusted-user",
                    association="CONTRIBUTOR",
                )
            ],
        )

    def test_member_and_collaborator_are_not_trusted(self) -> None:
        for association in ("MEMBER", "COLLABORATOR"):
            with self.subTest(association=association):
                self.assert_invalid(
                    make_body(),
                    [
                        make_base_plan(
                            author=TRUSTED_PLAN_AUTHOR.upper(),
                            association=association,
                        )
                    ],
                )

    def test_owner_login_comparison_is_case_insensitive(self) -> None:
        result = validate_pull_request(
            make_body(),
            REPOSITORY,
            make_issue(),
            [make_base_plan(author=TRUSTED_PLAN_AUTHOR.upper())],
        )
        self.assertIn("0 amendment(s)", result)

    def test_edited_trusted_base_fails(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(updated_at="2026-09-27T03:10:00Z")],
        )

    def test_edited_trusted_amendment_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [
                make_base_plan(),
                make_amendment(
                    amendment_id,
                    updated_at="2026-09-27T03:10:00Z",
                ),
            ],
        )

    def test_missing_comment_timestamps_fail(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        for missing_field in ("created_at", "updated_at"):
            with self.subTest(missing_field=missing_field):
                amendment = make_amendment(amendment_id)
                amendment.pop(missing_field)
                self.assert_invalid(
                    make_body(
                        amendment_permalinks=[issue_permalink(amendment_id)]
                    ),
                    [make_base_plan(), amendment],
                )

    def test_malformed_comment_html_url_fails(self) -> None:
        base_plan = make_base_plan()
        base_plan["html_url"] = "not-a-permalink"
        self.assert_invalid(make_body(), [base_plan])

    def test_comment_id_must_match_permalink_id(self) -> None:
        base_plan = make_base_plan()
        base_plan["id"] = BASE_COMMENT_ID + 1
        self.assert_invalid(make_body(), [base_plan])

    def test_multiple_trusted_base_plans_fail(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(), make_base_plan(BASE_COMMENT_ID + 1)],
        )

    def test_amendment_pointing_to_wrong_base_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        wrong_base = issue_permalink(BASE_COMMENT_ID + 2)
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), make_amendment(amendment_id, wrong_base)],
        )

    def test_wrong_amendment_marker_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = str(amendment["body"]).replace(
            AMENDMENT_MARKER,
            "# Not an Approved Implementation Plan Amendment",
            1,
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), amendment],
        )

    def test_amendment_missing_base_link_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = str(amendment["body"]).replace(
            f"Base Approved Implementation Plan: {BASE_PERMALINK}\n",
            "",
            1,
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), amendment],
        )

    def test_amendment_hidden_base_link_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = str(amendment["body"]).replace(
            f"Base Approved Implementation Plan: {BASE_PERMALINK}",
            f"<!--\nBase Approved Implementation Plan: {BASE_PERMALINK}\n-->",
            1,
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), amendment],
        )

    def test_amendment_fenced_base_link_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = str(amendment["body"]).replace(
            f"Base Approved Implementation Plan: {BASE_PERMALINK}",
            f"```\nBase Approved Implementation Plan: {BASE_PERMALINK}\n```",
            1,
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), amendment],
        )

    def test_amendment_indented_base_link_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = str(amendment["body"]).replace(
            f"Base Approved Implementation Plan: {BASE_PERMALINK}",
            f"    Base Approved Implementation Plan: {BASE_PERMALINK}",
            1,
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [make_base_plan(), amendment],
        )

    def test_amendment_base_permalink_to_another_issue_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        wrong_issue_permalink = BASE_PERMALINK.replace("/165#", "/164#")
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [
                make_base_plan(),
                make_amendment(amendment_id, wrong_issue_permalink),
            ],
        )

    def test_amendment_base_permalink_to_another_repository_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        wrong_repository_permalink = BASE_PERMALINK.replace(
            REPOSITORY,
            "other/repo",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [
                make_base_plan(),
                make_amendment(amendment_id, wrong_repository_permalink),
            ],
        )

    def test_amendment_permalink_to_another_issue_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        wrong_issue_permalink = issue_permalink(amendment_id).replace(
            "/165#",
            "/164#",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[wrong_issue_permalink]),
            [make_base_plan(), make_amendment(amendment_id)],
        )

    def test_amendment_permalink_to_another_repository_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        wrong_repository_permalink = issue_permalink(amendment_id).replace(
            REPOSITORY,
            "other/repo",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[wrong_repository_permalink]),
            [make_base_plan(), make_amendment(amendment_id)],
        )

    def test_amendment_template_placeholder_is_not_a_second_base_link(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        amendment = make_amendment(amendment_id)
        amendment["body"] = (
            str(amendment["body"])
            + "\nПример поля: Base Approved Implementation Plan: <permalink>\n"
        )
        result = validate_pull_request(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            REPOSITORY,
            make_issue(),
            [make_base_plan(), amendment],
        )
        self.assertIn("1 amendment(s)", result)

    def test_current_amendment_omitted_from_pr_fails(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(), make_amendment(BASE_COMMENT_ID + 1)],
        )

    def test_nonexistent_amendment_permalink_fails(self) -> None:
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(BASE_COMMENT_ID + 1)]),
            [make_base_plan()],
        )

    def test_duplicate_amendment_permalink_fails(self) -> None:
        amendment = issue_permalink(BASE_COMMENT_ID + 1)
        self.assert_invalid(
            make_body(amendment_permalinks=[amendment, amendment]),
            [make_base_plan(), make_amendment(BASE_COMMENT_ID + 1)],
        )

    def test_na_with_no_plan_or_amendments_succeeds(self) -> None:
        result = validate_pull_request(
            make_body("N/A"),
            REPOSITORY,
            make_issue(),
            [],
        )
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_na_with_trusted_plan_fails(self) -> None:
        self.assert_invalid(make_body("N/A"), [make_base_plan()])

    def test_na_with_trusted_amendment_fails(self) -> None:
        self.assert_invalid(
            make_body("N/A"),
            [make_base_plan(), make_amendment(BASE_COMMENT_ID + 1)],
        )

    def test_incident_marker_has_no_special_semantics(self) -> None:
        marker_comment = make_comment(
            BASE_COMMENT_ID + 1,
            "# Approved Plan Integrity Incident\n\nArbitrary historical text.",
        )
        result = validate_pull_request(
            make_body("N/A"),
            REPOSITORY,
            make_issue(),
            [marker_comment],
        )
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_public_api_failure_fails_closed(self) -> None:
        def fail_request(_request: object, timeout: int) -> object:
            raise OSError("network unavailable")

        with patch.dict(request_json.__globals__, {"urlopen": fail_request}):
            with self.assertRaises(ValidationError):
                request_json("https://api.github.com/repos/example/repo")

    def test_current_pull_request_rechecks_exact_head(self) -> None:
        current_pr = {
            "number": 200,
            "state": "open",
            "body": make_body(),
            "head": {"sha": "new-head"},
            "base": {"repo": {"full_name": REPOSITORY}},
        }
        with patch.dict(
            validate_current_pull_request.__globals__,
            {
                "fetch_current_pull_request": lambda *_args: current_pr,
            },
        ), self.assertRaises(ValidationError):
            validate_current_pull_request(
                REPOSITORY,
                200,
                expected_head_sha="old-head",
            )

    def test_untrusted_amendment_permalink_fails(self) -> None:
        amendment_id = BASE_COMMENT_ID + 1
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(amendment_id)]),
            [
                make_base_plan(),
                make_amendment(
                    amendment_id,
                    author="someone-else",
                    association="OWNER",
                ),
            ],
        )


if __name__ == "__main__":
    main()
