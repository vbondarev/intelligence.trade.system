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
INCIDENT_MARKER = validator["INCIDENT_MARKER"]
INTEGRITY_INCIDENT_AUTHOR = validator["INTEGRITY_INCIDENT_AUTHOR"]
TRUSTED_PLAN_AUTHOR = validator["TRUSTED_PLAN_AUTHOR"]
ValidationError = validator["ValidationError"]
fetch_current_pull_request = validator["fetch_current_pull_request"]
validate_current_pull_request = validator["validate_current_pull_request"]
validate_pull_request = validator["validate_pull_request"]
validation_snapshot = validator["validation_snapshot"]
request_json = validator["request_json"]

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


def make_incident(
    comment_id: int,
    affected_comment_id: int,
    artifact_type: str,
    action: str,
    *,
    created_at: str = "2026-09-27T03:00:00Z",
    updated_at: str | None = None,
) -> dict[str, object]:
    body = (
        f"{INCIDENT_MARKER}\n"
        f"Affected comment: {issue_permalink(affected_comment_id)}\n"
        f"Artifact type: {artifact_type}\n"
        f"Action: {action}\n"
        f"Recorded at: {created_at}\n"
    )
    return make_comment(
        comment_id,
        body,
        author=INTEGRITY_INCIDENT_AUTHOR,
        association="BOT",
        created_at=created_at,
        updated_at=updated_at,
    )


def make_base_plan(
    comment_id: int = BASE_COMMENT_ID,
    extra: str = "",
    **comment_values: str,
) -> dict[str, object]:
    return make_comment(
        comment_id,
        f"{BASE_PLAN_MARKER}\n\nPlan.\n{extra}",
        **comment_values,
    )


def make_amendment(
    comment_id: int,
    base_permalink: str = BASE_PERMALINK,
    extra: str = "",
    **comment_values: str,
) -> dict[str, object]:
    return make_comment(
        comment_id,
        f"{AMENDMENT_MARKER}\n\n"
        f"Base Approved Implementation Plan: {base_permalink}\n\n"
        f"Утверждённое изменение.\n{extra}",
        **comment_values,
    )


def make_base_replacement(
    comment_id: int,
    incident_id: int,
) -> dict[str, object]:
    incident_permalink = issue_permalink(incident_id)
    return make_base_plan(
        comment_id,
        extra=(
            "Supersedes Approved Plan Integrity Incident: "
            f"{incident_permalink}\n"
        ),
        created_at="2026-09-27T03:05:00Z",
    )


def make_amendment_replacement(
    comment_id: int,
    base_permalink: str,
    incident_id: int,
) -> dict[str, object]:
    incident_permalink = issue_permalink(incident_id)
    return make_amendment(
        comment_id,
        base_permalink,
        extra=(
            "Supersedes Approved Plan Integrity Incident: "
            f"{incident_permalink}\n"
        ),
        created_at="2026-09-27T03:05:00Z",
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
    if amendment_permalinks is None:
        amendment_values = ["Нет"]
    else:
        amendment_values = amendment_permalinks
    return (
        f"Closes #{ISSUE_NUMBER}\n\n"
        f"Approved Implementation Plan: {base_permalink}\n\n"
        "Approved Plan Amendments:\n"
        + "\n".join(f"- {value}" for value in amendment_values)
        + "\n"
    )


def make_pull_request(
    body: str | None = None,
    *,
    number: int = 200,
    head_sha: str = "head-sha",
    state: str = "open",
) -> dict[str, object]:
    return {
        "number": number,
        "state": state,
        "body": make_body() if body is None else body,
        "head": {"sha": head_sha, "ref": "task/165-example"},
        "base": {
            "ref": "develop",
            "repo": {"full_name": REPOSITORY},
        },
    }


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

    def test_valid_pr_with_base_plan_and_no_amendments(self) -> None:
        result = validate_pull_request(
            make_body(),
            REPOSITORY,
            make_issue(),
            [make_base_plan()],
        )
        self.assertIn("0 amendment(s)", result)

    def test_valid_pr_with_multiple_amendments(self) -> None:
        amendments = [make_amendment(5851955524), make_amendment(5851955525)]
        body = make_body(
            amendment_permalinks=[
                issue_permalink(5851955524),
                issue_permalink(5851955525),
            ]
        )
        result = validate_pull_request(
            body,
            REPOSITORY,
            make_issue(),
            [make_base_plan(), *amendments],
        )
        self.assertIn("2 amendment(s)", result)

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

    def test_missing_plan_field_fails(self) -> None:
        self.assert_invalid(
            make_body().replace(
                f"Approved Implementation Plan: {BASE_PERMALINK}\n",
                "",
            ),
            [make_base_plan()],
        )

    def test_malformed_plan_permalink_fails(self) -> None:
        self.assert_invalid(
            make_body("not-a-permalink"),
            [make_base_plan()],
        )

    def test_plan_permalink_to_another_repository_fails(self) -> None:
        self.assert_invalid(
            make_body(BASE_PERMALINK.replace(REPOSITORY, "other/repo")),
            [make_base_plan()],
        )

    def test_plan_permalink_to_another_issue_fails(self) -> None:
        self.assert_invalid(
            make_body(BASE_PERMALINK.replace("/165#", "/164#")),
            [make_base_plan()],
        )

    def test_linked_pull_request_is_rejected_as_what(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan()],
            make_issue(is_pull_request=True),
        )

    def test_untrusted_canonical_looking_base_does_not_break_na(self) -> None:
        result = validate_pull_request(
            make_body("N/A"),
            REPOSITORY,
            make_issue(),
            [
                make_comment(
                    BASE_COMMENT_ID,
                    f"{BASE_PLAN_MARKER}\n\nFake.",
                    author="untrusted-user",
                    association="CONTRIBUTOR",
                )
            ],
        )
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_pr_link_to_untrusted_base_comment_fails(self) -> None:
        untrusted = make_comment(
            BASE_COMMENT_ID,
            f"{BASE_PLAN_MARKER}\n\nFake.",
            author="untrusted-user",
            association="CONTRIBUTOR",
        )
        self.assert_invalid(make_body(), [untrusted])

    def test_untrusted_amendment_does_not_break_no_amendments_state(self) -> None:
        fake_amendment = make_amendment(
            5851955524,
            author="untrusted-user",
            association="MEMBER",
        )
        result = validate_pull_request(
            make_body(),
            REPOSITORY,
            make_issue(),
            [make_base_plan(), fake_amendment],
        )
        self.assertIn("0 amendment(s)", result)

    def test_member_or_collaborator_cannot_approve_plan(self) -> None:
        for association in ("MEMBER", "COLLABORATOR"):
            with self.subTest(association=association):
                untrusted = make_comment(
                    BASE_COMMENT_ID,
                    f"{BASE_PLAN_MARKER}\n\nPlan.",
                    author=TRUSTED_PLAN_AUTHOR,
                    association=association,
                )
                self.assert_invalid(make_body(), [untrusted])

    def test_multiple_trusted_base_plan_comments_fail(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(), make_base_plan(BASE_COMMENT_ID + 1)],
        )

    def test_na_without_plan_or_amendments_passes(self) -> None:
        result = validate_pull_request(
            make_body("N/A"),
            REPOSITORY,
            make_issue(),
            [],
        )
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_na_with_existing_plan_fails(self) -> None:
        self.assert_invalid(make_body("N/A"), [make_base_plan()])

    def test_na_with_orphaned_amendment_fails(self) -> None:
        self.assert_invalid(
            make_body("N/A"),
            [make_amendment(5851955524)],
        )

    def test_na_with_integrity_incident_fails(self) -> None:
        self.assert_invalid(
            make_body("N/A"),
            [make_incident(5851955526, BASE_COMMENT_ID, "base-plan", "deleted")],
        )

    def test_unlisted_amendment_fails(self) -> None:
        self.assert_invalid(
            make_body(),
            [make_base_plan(), make_amendment(5851955524)],
        )

    def test_amendment_link_to_another_issue_fails(self) -> None:
        amendment = make_amendment(5851955524)
        other_issue_url = issue_permalink(5851955524).replace("/165#", "/164#")
        self.assert_invalid(
            make_body(amendment_permalinks=[other_issue_url]),
            [make_base_plan(), amendment],
        )

    def test_listed_comment_with_wrong_amendment_marker_fails(self) -> None:
        wrong_comment = make_comment(5851955524, "# Не amendment\n")
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(5851955524)]),
            [make_base_plan(), wrong_comment],
        )

    def test_amendment_without_base_permalink_fails(self) -> None:
        amendment = make_comment(
            5851955524,
            f"{AMENDMENT_MARKER}\n\nУтверждённое изменение.",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(5851955524)]),
            [make_base_plan(), amendment],
        )

    def test_duplicate_amendment_permalink_fails(self) -> None:
        amendment = issue_permalink(5851955524)
        self.assert_invalid(
            make_body(amendment_permalinks=[amendment, amendment]),
            [make_base_plan(), make_amendment(5851955524)],
        )

    def test_modified_base_plan_without_incident_fails(self) -> None:
        changed_base = make_base_plan(
            updated_at="2026-09-27T03:10:00Z",
        )
        self.assert_invalid(make_body(), [changed_base])

    def test_deleted_base_requires_incident_and_replacement(self) -> None:
        incident = make_incident(
            5851955527,
            BASE_COMMENT_ID,
            "base-plan",
            "deleted",
        )
        self.assert_invalid(
            make_body("N/A"),
            [incident],
        )

    def test_deleted_base_recovers_only_with_linked_replacement(self) -> None:
        incident = make_incident(
            5851955527,
            BASE_COMMENT_ID,
            "base-plan",
            "deleted",
        )
        replacement = make_base_replacement(5851955528, 5851955527)
        result = validate_pull_request(
            make_body(issue_permalink(5851955528)),
            REPOSITORY,
            make_issue(),
            [incident, replacement],
        )
        self.assertIn("Approved Plan comment #5851955528", result)

    def test_edited_amendment_requires_linked_immutable_replacement(self) -> None:
        edited_amendment = make_amendment(
            5851955524,
            updated_at="2026-09-27T03:10:00Z",
        )
        incident = make_incident(
            5851955527,
            5851955524,
            "amendment",
            "edited",
        )
        replacement = make_amendment_replacement(
            5851955528,
            BASE_PERMALINK,
            5851955527,
        )
        result = validate_pull_request(
            make_body(amendment_permalinks=[issue_permalink(5851955528)]),
            REPOSITORY,
            make_issue(),
            [make_base_plan(), edited_amendment, incident, replacement],
        )
        self.assertIn("1 amendment(s)", result)

    def test_edited_amendment_without_incident_fails(self) -> None:
        edited_amendment = make_amendment(
            5851955524,
            updated_at="2026-09-27T03:10:00Z",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(5851955524)]),
            [make_base_plan(), edited_amendment],
        )

    def test_edited_amendment_without_replacement_fails(self) -> None:
        edited_amendment = make_amendment(
            5851955524,
            updated_at="2026-09-27T03:10:00Z",
        )
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(5851955524)]),
            [
                make_base_plan(),
                edited_amendment,
                make_incident(
                    5851955527,
                    5851955524,
                    "amendment",
                    "edited",
                ),
            ],
        )

    def test_deleted_amendment_link_without_incident_fails(self) -> None:
        self.assert_invalid(
            make_body(amendment_permalinks=[issue_permalink(5851955524)]),
            [make_base_plan()],
        )

    def test_untrusted_incident_comment_does_not_break_na(self) -> None:
        incident = make_incident(
            5851955527,
            BASE_COMMENT_ID,
            "base-plan",
            "deleted",
        )
        incident["user"] = {"login": "untrusted-user"}
        result = validate_pull_request(
            make_body("N/A"),
            REPOSITORY,
            make_issue(),
            [incident],
        )
        self.assertIn("Approved Plan и amendments отсутствуют", result)

    def test_incident_must_be_immutable(self) -> None:
        incident = make_incident(
            5851955527,
            BASE_COMMENT_ID,
            "base-plan",
            "deleted",
            updated_at="2026-09-27T03:10:00Z",
        )
        self.assert_invalid(make_body("N/A"), [incident])

    def test_current_pull_request_body_is_refetched_from_github(self) -> None:
        current_pr = make_pull_request()
        issue_comments = [make_base_plan()]
        with patch.dict(
            validate_current_pull_request.__globals__,
            {
                "request_json": lambda _url, _token=None: (
                    current_pr
                    if "/pulls/" in _url
                    else make_issue()
                    if "/issues/165" in _url and "/comments" not in _url
                    else issue_comments
                )
            },
        ):
            result = validate_current_pull_request(
                REPOSITORY,
                200,
                expected_head_sha="head-sha",
            )
            self.assertIn("Issue #165", result.message)

    def test_snapshot_tracks_trusted_metadata_but_ignores_untrusted_comments(self) -> None:
            pull_request = make_pull_request()
            base_plan = make_base_plan()
            original = validation_snapshot(pull_request, ISSUE_NUMBER, [base_plan])
            untrusted = make_comment(
                5851955530,
                "# Approved Implementation Plan — Amendment\n",
                author="untrusted-user",
                association="CONTRIBUTOR",
            )
            self.assertEqual(
                original,
                validation_snapshot(pull_request, ISSUE_NUMBER, [base_plan, untrusted]),
            )
            amendment = make_amendment(5851955524)
            self.assertNotEqual(
                original,
                validation_snapshot(pull_request, ISSUE_NUMBER, [base_plan, amendment]),
            )

    def test_stale_event_head_fails_closed(self) -> None:
        current_pr = make_pull_request(head_sha="new-head")
        with patch.dict(
            validate_current_pull_request.__globals__,
            {"fetch_current_pull_request": lambda *_args, **_kwargs: current_pr},
        ):
            with self.assertRaises(ValidationError):
                validate_current_pull_request(
                    REPOSITORY,
                    200,
                    expected_head_sha="old-head",
                )

    def test_current_pull_request_must_target_this_repository(self) -> None:
        pull_request = make_pull_request()
        pull_request["base"] = {
            "ref": "develop",
            "repo": {"full_name": "other/repo"},
        }
        with patch.dict(
            fetch_current_pull_request.__globals__,
            {"get_api_object": lambda *_args, **_kwargs: pull_request},
        ):
            with self.assertRaises(ValidationError):
                fetch_current_pull_request(REPOSITORY, 200)

    def test_public_request_has_no_authorization_header(self) -> None:
        class FakeResponse:
            def __enter__(self) -> "FakeResponse":
                return self

            def __exit__(self, *_args: object) -> None:
                return None

            def read(self) -> bytes:
                return b"{}"

        with patch.dict(
            request_json.__globals__,
            {"urlopen": lambda request, timeout: self.capture_request(request)},
        ):
            request_json("https://api.github.com/repos/example/repo")
        self.assertIsNone(self.captured_request.get_header("Authorization"))

    def capture_request(self, request: object) -> object:
        self.captured_request = request
        return self.fake_response()

    @staticmethod
    def fake_response() -> object:
        class FakeResponse:
            def __enter__(self) -> "FakeResponse":
                return self

            def __exit__(self, *_args: object) -> None:
                return None

            def read(self) -> bytes:
                return b"{}"

        return FakeResponse()

    def test_public_api_error_fails_closed(self) -> None:
        def fail_request(_request: object, timeout: int) -> object:
            raise OSError("network unavailable")

        with patch.dict(request_json.__globals__, {"urlopen": fail_request}):
            with self.assertRaises(ValidationError):
                request_json("https://api.github.com/repos/example/repo")


if __name__ == "__main__":
    main()
