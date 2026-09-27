#!/usr/bin/env python3
"""Regression tests trusted Plan freshness events and status updates."""

from __future__ import annotations

from pathlib import Path
import re
import runpy
from unittest import TestCase, main
from unittest.mock import patch


handler = runpy.run_path(
    str(Path(__file__).with_name("trusted-plan-freshness.py"))
)
BASE_PLAN_MARKER = handler["BASE_PLAN_MARKER"]
AMENDMENT_MARKER = handler["AMENDMENT_MARKER"]
INCIDENT_MARKER = handler["INCIDENT_MARKER"]
TRUSTED_PLAN_AUTHOR = handler["TRUSTED_PLAN_AUTHOR"]
INTEGRITY_INCIDENT_AUTHOR = handler["INTEGRITY_INCIDENT_AUTHOR"]
classify_issue_comment_event = handler["classify_issue_comment_event"]
workflow_run_targets = handler["workflow_run_targets"]
handle_workflow_run_event = handler["handle_workflow_run_event"]
handle_issue_comment_event = handler["handle_issue_comment_event"]
HandlerError = handler["HandlerError"]
write_status = handler["write_status"]
create_integrity_incident = handler["create_integrity_incident"]

REPOSITORY = "vbondarev/intelligence.trade.system"
ISSUE_NUMBER = 165
PULL_REQUEST_NUMBER = 166
HEAD_SHA = "a" * 40
BASE_URL = (
    f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
    "#issuecomment-5851955523"
)


def make_issue_event(
    action: str,
    body: str,
    *,
    author: str = TRUSTED_PLAN_AUTHOR,
    association: str = "OWNER",
    sender: str | None = None,
    changes: dict[str, object] | None = None,
    on_pull_request: bool = False,
) -> dict[str, object]:
    issue: dict[str, object] = {"number": ISSUE_NUMBER}
    if on_pull_request:
        issue["pull_request"] = {
            "url": f"https://api.github.com/repos/{REPOSITORY}/pulls/166"
        }
    event: dict[str, object] = {
        "action": action,
        "issue": issue,
        "comment": {
            "id": 6000000000,
            "body": body,
            "html_url": (
                f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
                "#issuecomment-6000000000"
            ),
            "author_association": association,
            "user": {"login": author},
        },
        "sender": {"login": sender or author},
    }
    if changes is not None:
        event["changes"] = changes
    return event


def make_workflow_run_event(
    action: str,
    *,
    event: str = "pull_request",
    head_sha: str = HEAD_SHA,
    pull_request_number: int = PULL_REQUEST_NUMBER,
) -> dict[str, object]:
    return {
        "action": action,
        "workflow_run": {
            "id": 700,
            "name": "Build and Test",
            "path": ".github/workflows/build.yml",
            "event": event,
            "pull_requests": [
                {
                    "number": pull_request_number,
                    "head": {"sha": head_sha},
                }
            ],
        },
    }


class TrustedPlanFreshnessTests(TestCase):
    def test_owner_created_base_plan_is_trusted_event(self) -> None:
        change = classify_issue_comment_event(
            make_issue_event("created", f"{BASE_PLAN_MARKER}\n\nPlan.")
            ,
            REPOSITORY,
        )
        self.assertIsNotNone(change)
        self.assertEqual("plan-created", change["kind"])
        self.assertEqual("base-plan", change["artifact_type"])

    def test_member_cannot_create_trusted_plan(self) -> None:
        change = classify_issue_comment_event(
            make_issue_event(
                "created",
                f"{BASE_PLAN_MARKER}\n\nPlan.",
                author=TRUSTED_PLAN_AUTHOR,
                association="MEMBER",
            ),
            REPOSITORY,
        )
        self.assertIsNone(change)

    def test_untrusted_canonical_looking_comment_is_ignored(self) -> None:
        change = classify_issue_comment_event(
            make_issue_event(
                "created",
                f"{BASE_PLAN_MARKER}\n\nFake.",
                author="untrusted-user",
                association="CONTRIBUTOR",
            ),
            REPOSITORY,
        )
        self.assertIsNone(change)

    def test_pr_comment_event_is_ignored(self) -> None:
        change = classify_issue_comment_event(
            make_issue_event(
                "created",
                f"{BASE_PLAN_MARKER}\n",
                on_pull_request=True,
            ),
            REPOSITORY,
        )
        self.assertIsNone(change)

    def test_owner_plan_edited_by_other_actor_is_invalidated(self) -> None:
        event = make_issue_event(
            "edited",
            "Updated Plan text",
            sender="collaborator",
            changes={"body": {"from": f"{BASE_PLAN_MARKER}\n\nOriginal."}},
        )
        change = classify_issue_comment_event(event, REPOSITORY)
        self.assertEqual("plan-edited", change["kind"])
        self.assertEqual("base-plan", change["artifact_type"])

    def test_owner_amendment_deleted_by_other_actor_is_invalidated(self) -> None:
        event = make_issue_event(
            "deleted",
            f"{AMENDMENT_MARKER}\n\nBase Approved Implementation Plan: {BASE_URL}",
            sender="collaborator",
        )
        change = classify_issue_comment_event(event, REPOSITORY)
        self.assertEqual("plan-deleted", change["kind"])
        self.assertEqual("amendment", change["artifact_type"])

    def test_unrelated_owner_comment_edit_is_ignored(self) -> None:
        event = make_issue_event(
            "edited",
            "Changed discussion comment.",
            changes={"body": {"from": "Previous discussion comment."}},
        )
        self.assertIsNone(classify_issue_comment_event(event, REPOSITORY))

    def test_bot_incident_creation_revalidates_without_approving_plan(self) -> None:
        event = make_issue_event(
            "created",
            f"{INCIDENT_MARKER}\nAffected comment: {BASE_URL}",
            author=INTEGRITY_INCIDENT_AUTHOR,
            association="BOT",
        )
        change = classify_issue_comment_event(event, REPOSITORY)
        self.assertEqual("incident-created", change["kind"])
        self.assertNotIn("artifact_type", change)

    def test_bot_incident_edit_is_a_failure_event(self) -> None:
        event = make_issue_event(
            "edited",
            "Tampered incident.",
            author=INTEGRITY_INCIDENT_AUTHOR,
            association="BOT",
            changes={"body": {"from": f"{INCIDENT_MARKER}\nAffected comment: {BASE_URL}"}},
        )
        change = classify_issue_comment_event(event, REPOSITORY)
        self.assertEqual("incident-mutated", change["kind"])

    def test_workflow_run_targets_only_current_head(self) -> None:
        event = make_workflow_run_event("requested")

        def current_head(_repository: str, _number: int, _token: str) -> str:
            return HEAD_SHA

        with patch.dict(
            workflow_run_targets.__globals__,
            {"open_pull_request_head": current_head},
        ):
            self.assertEqual(
                [(PULL_REQUEST_NUMBER, HEAD_SHA)],
                workflow_run_targets(
                    REPOSITORY,
                    event["workflow_run"],
                    "read-only-trusted-token",
                ),
            )

    def test_stale_workflow_run_does_not_update_current_head(self) -> None:
        event = make_workflow_run_event("requested", head_sha="b" * 40)

        with patch.dict(
            workflow_run_targets.__globals__,
            {"open_pull_request_head": lambda *_args: HEAD_SHA},
        ):
            self.assertEqual(
                [],
                workflow_run_targets(
                    REPOSITORY,
                    event["workflow_run"],
                    "read-only-trusted-token",
                ),
            )

    def test_non_pull_request_workflow_run_is_ignored(self) -> None:
        event = make_workflow_run_event("completed", event="push")
        with patch.dict(
            workflow_run_targets.__globals__,
            {"open_pull_request_head": lambda *_args: self.fail("must not query PR")},
        ):
            self.assertEqual(
                [],
                workflow_run_targets(
                    REPOSITORY,
                    event["workflow_run"],
                    "read-only-trusted-token",
                ),
            )

    def test_unrelated_workflow_run_is_ignored(self) -> None:
        event = make_workflow_run_event("completed")
        event["workflow_run"]["path"] = ".github/workflows/other.yml"
        with patch.dict(
            workflow_run_targets.__globals__,
            {"open_pull_request_head": lambda *_args: self.fail("must not query PR")},
        ):
            self.assertEqual(
                [],
                workflow_run_targets(
                    REPOSITORY,
                    event["workflow_run"],
                    "read-only-trusted-token",
                ),
            )

    def test_requested_event_sets_pending_without_validating(self) -> None:
        event = make_workflow_run_event("requested")
        calls: list[tuple[str, str]] = []
        with (
            patch.dict(
                handle_workflow_run_event.__globals__,
                {
                    "workflow_run_targets": lambda *_args: [
                        (PULL_REQUEST_NUMBER, HEAD_SHA)
                    ],
                    "write_status": lambda _repo, _sha, state, _desc, _token:
                    calls.append(("status", state)),
                    "publish_validation_result": lambda *_args: self.fail(
                        "requested must not validate"
                    ),
                },
            )
        ):
            result = handle_workflow_run_event(
                event,
                REPOSITORY,
                "trusted-token",
            )
        self.assertIn("pending", result)
        self.assertEqual([("status", "pending")], calls)

    def test_completed_event_validates_after_pending(self) -> None:
        event = make_workflow_run_event("completed")
        calls: list[str] = []

        with patch.dict(
            handle_workflow_run_event.__globals__,
            {
                "workflow_run_targets": lambda *_args: [
                    (PULL_REQUEST_NUMBER, HEAD_SHA)
                ],
                "set_pending_for_targets": lambda *_args: calls.append("pending"),
                "publish_validation_result": lambda *_args: calls.append("validate"),
            },
        ):
            handle_workflow_run_event(event, REPOSITORY, "trusted-token")
        self.assertEqual(["pending", "validate"], calls)

    def test_issue_event_sets_pending_before_validation(self) -> None:
        event = make_issue_event("created", f"{BASE_PLAN_MARKER}\n\nPlan.")
        calls: list[str] = []

        def write_status(
            _repo: str,
            _sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append(f"status:{state}")

        def publish(
            _repo: str,
            _number: int,
            _sha: str,
            _token: str,
        ) -> None:
            calls.append("validate")

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: [
                    (PULL_REQUEST_NUMBER, HEAD_SHA)
                ],
                "set_pending_for_targets": lambda _repo, targets, token: [
                    write_status(_repo, sha, "pending", "pending", token)
                    for _, sha in targets
                ],
                "publish_validation_result": publish,
            },
        ):
            handle_issue_comment_event(
                event,
                REPOSITORY,
                "trusted-token",
            )
        self.assertEqual(["status:pending", "validate"], calls)

    def test_classification_error_invalidates_existing_success(self) -> None:
        event = make_issue_event("deleted", f"{BASE_PLAN_MARKER}\n\nPlan.")
        event["comment"].pop("body")
        states = {HEAD_SHA: "success"}
        calls: list[str] = []

        def write_status(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append(state)
            states[sha] = state

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: [
                    (PULL_REQUEST_NUMBER, HEAD_SHA)
                ],
                "write_status": write_status,
                "publish_validation_result": lambda *_args: self.fail(
                    "classification errors must not publish a success"
                ),
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "Deleted comment payload не содержит исходный body",
            ) as caught:
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertNotIsInstance(caught.exception, NameError)
        self.assertEqual(["pending", "failure"], calls)
        self.assertEqual("failure", states[HEAD_SHA])

    def test_status_api_error_still_attempts_failure_and_never_succeeds(
        self,
    ) -> None:
        event = make_issue_event("deleted", f"{BASE_PLAN_MARKER}\n\nPlan.")
        event["comment"].pop("body")
        states = {HEAD_SHA: "success"}
        calls: list[str] = []

        def write_status(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append(state)
            if state == "pending":
                raise HandlerError("GitHub status API недоступен.")
            states[sha] = state

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: [
                    (PULL_REQUEST_NUMBER, HEAD_SHA)
                ],
                "write_status": write_status,
                "publish_validation_result": lambda *_args: self.fail(
                    "status API errors must not publish a success"
                ),
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "Deleted comment payload не содержит исходный body",
            ) as caught:
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertNotIsInstance(caught.exception, NameError)
        self.assertIsInstance(caught.exception.__cause__, HandlerError)
        self.assertEqual(
            "GitHub status API недоступен.",
            str(caught.exception.__cause__),
        )
        self.assertEqual(["pending", "failure"], calls)
        self.assertEqual("failure", states[HEAD_SHA])

    def test_owner_plan_deletion_by_other_actor_creates_incident(self) -> None:
        event = make_issue_event(
            "deleted",
            f"{BASE_PLAN_MARKER}\n\nPlan.",
            sender="collaborator",
        )
        calls: list[str] = []

        def publish_incident(
            _repo: str,
            change: dict[str, object],
            _token: str,
        ) -> dict[str, object]:
            self.assertEqual("plan-deleted", change["kind"])
            calls.append("incident")
            return {}

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: [
                    (PULL_REQUEST_NUMBER, HEAD_SHA)
                ],
                "set_pending_for_targets": lambda *_args: calls.append("pending"),
                "create_integrity_incident": publish_incident,
                "publish_validation_result": lambda *_args: calls.append(
                    "validation"
                ),
            },
        ):
            handle_issue_comment_event(event, REPOSITORY, "trusted-token")
        self.assertEqual(["pending", "incident", "validation"], calls)

    def test_integrity_incident_is_append_only_bot_comment(self) -> None:
        change = {
            "issue_number": ISSUE_NUMBER,
            "comment_url": BASE_URL,
            "artifact_type": "base-plan",
            "kind": "plan-deleted",
        }
        stored: dict[str, object] = {}

        def request(
            _url: str,
            _token: str,
            *,
            method: str = "GET",
            payload: dict[str, object] | None = None,
        ) -> object:
            if method == "POST":
                body = payload["body"]
                self.assertIn(INCIDENT_MARKER, body)
                self.assertIn("Affected comment: " + BASE_URL, body)
                self.assertIn("Artifact type: base-plan", body)
                self.assertIn("Action: deleted", body)
                self.assertIn("Recorded at:", body)
                stored.update(
                    {
                        "id": 6000000001,
                        "body": body,
                        "html_url": (
                            f"https://github.com/{REPOSITORY}/issues/{ISSUE_NUMBER}"
                            "#issuecomment-6000000001"
                        ),
                        "user": {"login": INTEGRITY_INCIDENT_AUTHOR},
                        "created_at": "2026-09-27T04:00:00Z",
                        "updated_at": "2026-09-27T04:00:00Z",
                    }
                )
                return stored
            raise AssertionError(f"Unexpected API request: {method} {_url}")

        with patch.dict(
            create_integrity_incident.__globals__,
            {
                "validator_comments": lambda *_args: [],
                "request_json": request,
                "get_object": lambda *_args: stored,
            },
        ):
            incident = create_integrity_incident(
                REPOSITORY,
                change,
                "trusted-token",
            )
        self.assertEqual(INTEGRITY_INCIDENT_AUTHOR, incident["user"]["login"])
        self.assertEqual(incident["created_at"], incident["updated_at"])

    def test_workflow_status_api_failure_fails_closed(self) -> None:
        def fail_api(*_args, **_kwargs) -> object:
            raise HandlerError("API недоступен")

        with patch.dict(write_status.__globals__, {"request_json": fail_api}):
            with self.assertRaises(HandlerError):
                write_status(REPOSITORY, HEAD_SHA, "pending", "pending", "token")

    def test_workflow_file_uses_trusted_main_and_no_pull_request_target(self) -> None:
        workflow = (
            Path(__file__).resolve().parent.parent
            / "workflows"
            / "trusted-plan-freshness.yml"
        ).read_text(encoding="utf-8")
        candidate_job_match = re.search(
            r"(?ms)^  test-pr-head:\n(?P<job>.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)",
            workflow,
        )
        self.assertIsNotNone(candidate_job_match)
        candidate_job = candidate_job_match.group("job")
        self.assertIn("workflow_run:", workflow)
        self.assertIn("types: [ requested, completed ]", workflow)
        self.assertIn("issue_comment:", workflow)
        self.assertIn("ref: ${{ github.event.repository.default_branch }}", workflow)
        self.assertIn("permissions: {}", workflow)
        self.assertIn("statuses: write", workflow)
        self.assertIn("issues: write", workflow)
        self.assertIn("GITHUB_TOKEN: ${{ github.token }}", workflow)
        self.assertNotIn("pull_request_target:", workflow)
        self.assertIn("permissions: {}", candidate_job)
        self.assertIn("refs/pull/${PR_NUMBER}/head", candidate_job)
        self.assertIn('"$fetched_sha" != "$EXPECTED_HEAD_SHA"', candidate_job)
        self.assertNotIn("actions/checkout", candidate_job)
        self.assertNotIn("GITHUB_TOKEN", candidate_job)
        self.assertNotIn("github.token", candidate_job)
        self.assertEqual(1, workflow.count("github.event.pull_request.head.sha"))
        self.assertNotIn("download-artifact", workflow)


if __name__ == "__main__":
    main()
