#!/usr/bin/env python3
"""Самодостаточные regression tests validator Approved Plan для PR."""

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
ValidationError = validator["ValidationError"]
fetch_issue_comments = validator["fetch_issue_comments"]
validate_pull_request = validator["validate_pull_request"]

REPOSITORY = "vbondarev/intelligence.trade.system"
ISSUE_NUMBER = 165
BASE_COMMENT_ID = 5851955523
BASE_PERMALINK = (
    f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
    f"#issuecomment-{BASE_COMMENT_ID}"
)


def make_comment(comment_id: int, body: str) -> dict[str, object]:
    return {
        "id": comment_id,
        "body": body,
        "html_url": (
            f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
            f"#issuecomment-{comment_id}"
        ),
    }


def make_amendment(comment_id: int, base_permalink: str = BASE_PERMALINK) -> dict[str, object]:
    return make_comment(
        comment_id,
        f"{AMENDMENT_MARKER}\n\n"
        f"Base Approved Implementation Plan: {base_permalink}\n\n"
        "Утверждённое изменение.",
    )


def make_event(
    body: str | None = None,
) -> dict[str, object]:
    if body is None:
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            "- Нет\n"
        )
    return {"pull_request": {"number": 200, "body": body}}


class PullRequestPlanValidatorTests(TestCase):
    def assert_invalid(
        self,
        event: dict[str, object],
        comments: list[dict[str, object]],
    ) -> None:
        with self.assertRaises(ValidationError):
            validate_pull_request(event, REPOSITORY, comments)

    def test_valid_pr_with_base_plan_and_no_amendments(self) -> None:
        result = validate_pull_request(
            make_event(),
            REPOSITORY,
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n\nПлан.")],
        )
        self.assertIn("0 amendment(s)", result)

    def test_valid_pr_with_multiple_amendments(self) -> None:
        amendments = [make_amendment(5851955524), make_amendment(5851955525)]
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            + "\n".join(f"- {comment['html_url']}" for comment in amendments)
        )
        result = validate_pull_request(
            make_event(body),
            REPOSITORY,
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n\nПлан."),
                *amendments,
            ],
        )
        self.assertIn("2 amendment(s)", result)

    def test_missing_closes_fails(self) -> None:
        body = make_event()["pull_request"]["body"]
        self.assert_invalid(
            make_event(body.replace(f"Closes #{ISSUE_NUMBER}\n", "")),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_multiple_primary_closes_fail(self) -> None:
        body = make_event()["pull_request"]["body"]
        self.assert_invalid(
            make_event(body.replace(
                f"Closes #{ISSUE_NUMBER}",
                f"Closes #{ISSUE_NUMBER}\nCloses #164",
            )),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_missing_plan_field_fails(self) -> None:
        body = make_event()["pull_request"]["body"]
        self.assert_invalid(
            make_event(body.replace(
                f"Approved Implementation Plan: {BASE_PERMALINK}\n",
                "",
            )),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_malformed_plan_permalink_fails(self) -> None:
        body = make_event()["pull_request"]["body"].replace(
            BASE_PERMALINK,
            "not-a-permalink",
        )
        self.assert_invalid(
            make_event(body),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_plan_permalink_to_another_repository_fails(self) -> None:
        other_repository_url = BASE_PERMALINK.replace(REPOSITORY, "other/repo")
        body = make_event()["pull_request"]["body"].replace(
            BASE_PERMALINK,
            other_repository_url,
        )
        self.assert_invalid(
            make_event(body),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_plan_permalink_to_another_issue_fails(self) -> None:
        other_issue_url = BASE_PERMALINK.replace("/165#", "/164#")
        body = make_event()["pull_request"]["body"].replace(
            BASE_PERMALINK,
            other_issue_url,
        )
        self.assert_invalid(
            make_event(body),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_missing_base_comment_fails(self) -> None:
        self.assert_invalid(make_event(), [])

    def test_target_comment_with_wrong_marker_fails(self) -> None:
        self.assert_invalid(
            make_event(),
            [make_comment(BASE_COMMENT_ID, "# Другой комментарий\n")],
        )

    def test_multiple_base_plan_comments_fail(self) -> None:
        self.assert_invalid(
            make_event(),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                make_comment(BASE_COMMENT_ID + 1, f"{BASE_PLAN_MARKER}\n"),
            ],
        )

    def test_na_without_plan_or_amendments_passes(self) -> None:
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            "Approved Implementation Plan: N/A\n\n"
            "Approved Plan Amendments:\n"
            "- Нет\n"
        )
        result = validate_pull_request(make_event(body), REPOSITORY, [])
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_na_with_existing_plan_fails(self) -> None:
        body = make_event()["pull_request"]["body"].replace(
            BASE_PERMALINK,
            "N/A",
        )
        self.assert_invalid(
            make_event(body),
            [make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n")],
        )

    def test_na_with_orphaned_amendment_fails(self) -> None:
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            "Approved Implementation Plan: N/A\n\n"
            "Approved Plan Amendments:\n"
            "- Нет\n"
        )
        self.assert_invalid(make_event(body), [make_amendment(5851955524)])

    def test_no_amendment_marker_with_unlisted_amendment_fails(self) -> None:
        self.assert_invalid(
            make_event(),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                make_amendment(5851955524),
            ],
        )

    def test_amendment_link_to_another_issue_fails(self) -> None:
        amendment = make_amendment(5851955524)
        other_issue_url = str(amendment["html_url"]).replace("/165#", "/164#")
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            f"- {other_issue_url}\n"
        )
        self.assert_invalid(
            make_event(body),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                amendment,
            ],
        )

    def test_listed_comment_with_wrong_amendment_marker_fails(self) -> None:
        amendment = make_comment(5851955524, "# Не amendment\n")
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            f"- {amendment['html_url']}\n"
        )
        self.assert_invalid(
            make_event(body),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                amendment,
            ],
        )

    def test_amendment_without_base_permalink_fails(self) -> None:
        amendment = make_comment(
            5851955524,
            f"{AMENDMENT_MARKER}\n\nУтверждённое изменение.",
        )
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            f"- {amendment['html_url']}\n"
        )
        self.assert_invalid(
            make_event(body),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                amendment,
            ],
        )

    def test_duplicate_amendment_permalink_fails(self) -> None:
        amendment = make_amendment(5851955524)
        body = (
            f"Closes #{ISSUE_NUMBER}\n\n"
            f"Approved Implementation Plan: {BASE_PERMALINK}\n\n"
            "Approved Plan Amendments:\n"
            f"- {amendment['html_url']}\n"
            f"- {amendment['html_url']}\n"
        )
        self.assert_invalid(
            make_event(body),
            [
                make_comment(BASE_COMMENT_ID, f"{BASE_PLAN_MARKER}\n"),
                amendment,
            ],
        )

    def test_github_api_error_fails_closed(self) -> None:
        def fail_request(_url: str, _token: str) -> object:
            raise ValidationError("API недоступен")

        with patch.dict(
            fetch_issue_comments.__globals__,
            {"request_json": fail_request},
        ):
            with self.assertRaises(ValidationError):
                fetch_issue_comments(REPOSITORY, ISSUE_NUMBER, "read-only-token")


if __name__ == "__main__":
    main()
