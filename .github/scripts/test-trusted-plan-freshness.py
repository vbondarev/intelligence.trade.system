#!/usr/bin/env python3
"""Regression tests trusted Plan freshness events and status updates."""

from __future__ import annotations

from pathlib import Path
import re
import runpy
from types import SimpleNamespace
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
ValidationError = handler["ValidationError"]
write_status = handler["write_status"]
create_integrity_incident = handler["create_integrity_incident"]
publish_validation_result = handler["publish_validation_result"]

REPOSITORY = "vbondarev/intelligence.trade.system"
ISSUE_NUMBER = 165
PULL_REQUEST_NUMBER = 166
HEAD_SHA = "a" * 40
MULTI_TARGETS = [
    (166, "a" * 40),
    (167, "b" * 40),
    (168, "c" * 40),
]
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

    def test_workflow_run_partial_pending_failure_fails_every_target(self) -> None:
        event = make_workflow_run_event("completed")
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))
            if sha == MULTI_TARGETS[0][1] and state == "pending":
                raise HandlerError("pending A failed")

        with patch.dict(
            handle_workflow_run_event.__globals__,
            {
                "workflow_run_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": lambda *_args: self.fail(
                    "partial pending failure must not validate"
                ),
            },
        ):
            with self.assertRaisesRegex(HandlerError, "pending A failed"):
                handle_workflow_run_event(event, REPOSITORY, "trusted-token")

        self.assertEqual(
            [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [(sha, "failure") for _, sha in MULTI_TARGETS],
            calls,
        )

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

    def test_partial_pending_failure_fails_all_targets(self) -> None:
        event = make_issue_event("created", f"{BASE_PLAN_MARKER}\n\nPlan.")
        calls: list[tuple[str, str]] = []
        states = {sha: "success" for _, sha in MULTI_TARGETS}

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))
            if sha == MULTI_TARGETS[0][1] and state == "pending":
                raise HandlerError("pending A failed")
            states[sha] = state

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": lambda *_args: self.fail(
                    "partial pending failure must not validate"
                ),
            },
        ):
            with self.assertRaisesRegex(HandlerError, "pending A failed"):
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertEqual(
            [
                (MULTI_TARGETS[0][1], "pending"),
                (MULTI_TARGETS[1][1], "pending"),
                (MULTI_TARGETS[2][1], "pending"),
                (MULTI_TARGETS[0][1], "failure"),
                (MULTI_TARGETS[1][1], "failure"),
                (MULTI_TARGETS[2][1], "failure"),
            ],
            calls,
        )
        self.assertEqual({sha: "failure" for _, sha in MULTI_TARGETS}, states)
        self.assertNotIn("success", [state for _, state in calls])

    def test_failure_status_error_does_not_skip_remaining_targets(self) -> None:
        event = make_issue_event(
            "edited",
            f"{INCIDENT_MARKER}\n\nMutated.",
            author=INTEGRITY_INCIDENT_AUTHOR,
            association="BOT",
            changes={"body": {"from": f"{INCIDENT_MARKER}\n\nOriginal."}},
        )
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))
            if sha == MULTI_TARGETS[0][1] and state == "failure":
                raise HandlerError("failure A failed")

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": lambda *_args: self.fail(
                    "mutated incident must not validate"
                ),
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "Integrity Incident был изменён или удалён",
            ) as caught:
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertIsInstance(caught.exception.__cause__, HandlerError)
        self.assertEqual("failure A failed", str(caught.exception.__cause__))
        self.assertEqual(
            [
                (sha, "pending") for _, sha in MULTI_TARGETS
            ]
            + [
                (sha, "failure") for _, sha in MULTI_TARGETS
            ],
            calls,
        )
        self.assertNotIn("success", [state for _, state in calls])

    def test_incident_creation_and_first_failure_errors_do_not_skip_targets(
        self,
    ) -> None:
        event = make_issue_event(
            "deleted",
            f"{BASE_PLAN_MARKER}\n\nPlan.",
            sender="collaborator",
        )
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))
            if sha == MULTI_TARGETS[0][1] and state == "failure":
                raise HandlerError("failure A failed")

        def fail_incident(*_args) -> None:
            raise HandlerError("incident creation failed")

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "create_integrity_incident": fail_incident,
                "publish_validation_result": lambda *_args: self.fail(
                    "failed incident creation must not validate"
                ),
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "incident creation failed",
            ) as caught:
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertIsInstance(caught.exception.__cause__, HandlerError)
        self.assertEqual("failure A failed", str(caught.exception.__cause__))
        self.assertEqual(
            [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [(sha, "failure") for _, sha in MULTI_TARGETS],
            calls,
        )
        self.assertNotIn("success", [state for _, state in calls])

    def test_classification_error_attempts_all_pending_and_failure_targets(
        self,
    ) -> None:
        event = make_issue_event("deleted", f"{BASE_PLAN_MARKER}\n\nPlan.")
        event["comment"].pop("body")
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))
            if sha == MULTI_TARGETS[0][1]:
                raise HandlerError(f"{state} A failed")

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": lambda *_args: self.fail(
                    "classification failure must not validate"
                ),
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "Deleted comment payload не содержит исходный body",
            ) as caught:
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertEqual(
            [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [(sha, "failure") for _, sha in MULTI_TARGETS],
            calls,
        )
        self.assertIsInstance(caught.exception.__cause__, HandlerError)
        self.assertEqual("pending A failed", str(caught.exception.__cause__))
        self.assertNotIn("success", [state for _, state in calls])

    def test_multi_target_happy_path_sets_pending_then_validates_all(self) -> None:
        event = make_issue_event("created", f"{BASE_PLAN_MARKER}\n\nPlan.")
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))

        def publish(
            _repo: str,
            number: int,
            sha: str,
            _token: str,
        ) -> None:
            calls.append((sha, f"validate:{number}"))

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": publish,
            },
        ):
            result = handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertIn("проверено PR: 3", result)
        self.assertEqual(
            [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [
                (sha, f"validate:{number}")
                for number, sha in MULTI_TARGETS
            ],
            calls,
        )

    def test_validation_error_does_not_skip_other_targets(self) -> None:
        event = make_issue_event("created", f"{BASE_PLAN_MARKER}\n\nPlan.")
        calls: list[tuple[str, str]] = []

        def write(
            _repo: str,
            sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append((sha, state))

        def publish(
            _repo: str,
            number: int,
            sha: str,
            _token: str,
        ) -> None:
            calls.append((sha, f"validate:{number}"))
            if number == MULTI_TARGETS[0][0]:
                raise HandlerError("validation A failed")

        with patch.dict(
            handle_issue_comment_event.__globals__,
            {
                "linked_pull_request_targets": lambda *_args: MULTI_TARGETS,
                "write_status": write,
                "publish_validation_result": publish,
            },
        ):
            with self.assertRaisesRegex(HandlerError, "validation A failed"):
                handle_issue_comment_event(event, REPOSITORY, "trusted-token")

        self.assertEqual(
            [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [
                (sha, f"validate:{number}")
                for number, sha in MULTI_TARGETS
            ]
            + [(sha, "pending") for _, sha in MULTI_TARGETS]
            + [(sha, "failure") for _, sha in MULTI_TARGETS],
            calls,
        )
        self.assertNotIn("success", [state for _, state in calls])

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

    def test_success_is_published_only_after_matching_final_snapshot(self) -> None:
        calls: list[str] = []
        statuses: list[str] = ["pending"]
        snapshot = SimpleNamespace(snapshot_sha256="a" * 64)

        def write(
            _repository: str,
            _sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            calls.append(f"status:{state}")
            statuses.append(state)

        def validate(*_args, **_kwargs):
            calls.append("final-validation")
            return snapshot

        with patch.dict(
            publish_validation_result.__globals__,
            {
                "ensure_pull_request_edit_event": lambda *_args: calls.append(
                    "confirm-edit-state"
                ),
                "stable_validation": lambda *_args: (
                    calls.append("stable-validation") or snapshot
                ),
                "validate_current_pull_request": validate,
                "write_status": write,
            },
        ):
            publish_validation_result(
                REPOSITORY,
                PULL_REQUEST_NUMBER,
                HEAD_SHA,
                "trusted-token",
            )

        self.assertEqual(["pending", "success"], statuses)
        self.assertEqual(
            [
                "confirm-edit-state",
                "stable-validation",
                "confirm-edit-state",
                "final-validation",
                "status:success",
            ],
            calls,
        )
        self.assertEqual("status:success", calls[-1])
        self.assertEqual(1, calls.count("final-validation"))

    def test_final_validation_error_never_publishes_success(self) -> None:
        statuses: list[str] = ["pending"]

        def write(
            _repository: str,
            _sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            statuses.append(state)

        def fail_validation(*_args, **_kwargs):
            raise ValidationError("Plan metadata is invalid.")

        with patch.dict(
            publish_validation_result.__globals__,
            {
                "ensure_pull_request_edit_event": lambda *_args: None,
                "stable_validation": lambda *_args: SimpleNamespace(
                    snapshot_sha256="a" * 64
                ),
                "validate_current_pull_request": fail_validation,
                "write_status": write,
            },
        ):
            publish_validation_result(
                REPOSITORY,
                PULL_REQUEST_NUMBER,
                HEAD_SHA,
                "trusted-token",
            )

        self.assertEqual(["pending", "failure"], statuses)
        self.assertNotIn("success", statuses)

    def test_changing_final_snapshots_retry_and_exhaust_as_failure(self) -> None:
        statuses: list[str] = ["pending"]
        final_snapshots = iter(["b" * 64, "c" * 64, "d" * 64])
        validation_calls = 0

        def validate(*_args, **_kwargs):
            nonlocal validation_calls
            validation_calls += 1
            return SimpleNamespace(snapshot_sha256=next(final_snapshots))

        with patch.dict(
            publish_validation_result.__globals__,
            {
                "ensure_pull_request_edit_event": lambda *_args: None,
                "stable_validation": lambda *_args: SimpleNamespace(
                    snapshot_sha256="a" * 64
                ),
                "validate_current_pull_request": validate,
                "write_status": lambda _repo, _sha, state, _desc, _token:
                statuses.append(state),
            },
        ):
            publish_validation_result(
                REPOSITORY,
                PULL_REQUEST_NUMBER,
                HEAD_SHA,
                "trusted-token",
            )

        self.assertEqual(
            ["pending", "pending", "pending", "pending", "failure"],
            statuses,
        )
        self.assertEqual(3, validation_calls)
        self.assertNotIn("success", statuses)

    def test_failure_status_error_propagates_after_final_validation_error(
        self,
    ) -> None:
        attempted_statuses: list[str] = []
        persisted_statuses: list[str] = ["pending"]

        def write(
            _repository: str,
            _sha: str,
            state: str,
            _description: str,
            _token: str,
        ) -> None:
            attempted_statuses.append(state)
            if state == "failure":
                raise HandlerError("Status API unavailable.")
            persisted_statuses.append(state)

        def fail_validation(*_args, **_kwargs):
            raise HandlerError("Metadata API unavailable.")

        with patch.dict(
            publish_validation_result.__globals__,
            {
                "ensure_pull_request_edit_event": lambda *_args: None,
                "stable_validation": lambda *_args: SimpleNamespace(
                    snapshot_sha256="a" * 64
                ),
                "validate_current_pull_request": fail_validation,
                "write_status": write,
            },
        ):
            with self.assertRaisesRegex(
                HandlerError,
                "Status API unavailable",
            ):
                publish_validation_result(
                    REPOSITORY,
                    PULL_REQUEST_NUMBER,
                    HEAD_SHA,
                    "trusted-token",
                )

        self.assertEqual(["failure"], attempted_statuses)
        self.assertEqual(["pending"], persisted_statuses)
        self.assertNotIn("success", attempted_statuses)

    def test_snapshot_stabilizing_on_retry_can_publish_success(self) -> None:
        statuses: list[str] = ["pending"]
        stable_snapshots = iter(["a" * 64, "b" * 64])
        final_snapshots = iter(["b" * 64, "b" * 64])
        stable_calls = 0

        def stable(*_args):
            nonlocal stable_calls
            stable_calls += 1
            return SimpleNamespace(snapshot_sha256=next(stable_snapshots))

        def validate(*_args, **_kwargs):
            return SimpleNamespace(snapshot_sha256=next(final_snapshots))

        with patch.dict(
            publish_validation_result.__globals__,
            {
                "ensure_pull_request_edit_event": lambda *_args: None,
                "stable_validation": stable,
                "validate_current_pull_request": validate,
                "write_status": lambda _repo, _sha, state, _desc, _token:
                statuses.append(state),
            },
        ):
            publish_validation_result(
                REPOSITORY,
                PULL_REQUEST_NUMBER,
                HEAD_SHA,
                "trusted-token",
            )

        self.assertEqual(["pending", "pending", "success"], statuses)
        self.assertEqual(2, stable_calls)
        self.assertEqual("success", statuses[-1])

    def test_trusted_workflow_only_handles_authenticated_events(self) -> None:
        workflow = (
            Path(__file__).resolve().parent.parent
            / "workflows"
            / "trusted-plan-freshness.yml"
        ).read_text(encoding="utf-8")
        self.assertRegex(workflow, r"(?m)^  workflow_run:\s*$")
        self.assertRegex(workflow, r"(?m)^  issue_comment:\s*$")
        self.assertNotRegex(workflow, r"(?m)^  pull_request(?:_target)?:\s*$")
        self.assertNotIn("pull_request:", workflow)
        self.assertNotIn("pull_request_target:", workflow)
        self.assertNotIn("test-pr-head", workflow)
        self.assertNotIn("refs/pull/", workflow)
        self.assertNotIn("PR_NUMBER", workflow)
        self.assertNotIn("EXPECTED_HEAD_SHA", workflow)
        self.assertNotIn("test-check-pr-approved-plan.py", workflow)
        self.assertNotIn("test-trusted-plan-freshness.py", workflow)
        self.assertNotIn("test-pr-workflow-security.py", workflow)

        events_match = re.search(
            r"(?ms)^on:\n(?P<events>.*?)(?=^permissions:)",
            workflow,
        )
        self.assertIsNotNone(events_match)
        events = re.findall(
            r"(?m)^  ([A-Za-z0-9_-]+):\s*$",
            events_match.group("events"),
        )
        self.assertEqual(["workflow_run", "issue_comment"], events)
        self.assertIn("types: [ requested, completed ]", events_match.group("events"))
        self.assertIn(
            "types: [ created, edited, deleted ]",
            events_match.group("events"),
        )

        self.assertIn("jobs:\n  validate-workflow-run:", workflow)
        self.assertIn("  validate-issue-comment:", workflow)
        self.assertNotIn("  test-pr-head:", workflow)
        self.assertIn("ref: ${{ github.event.repository.default_branch }}", workflow)
        self.assertIn("permissions: {}", workflow)
        self.assertIn("statuses: write", workflow)
        self.assertIn("issues: write", workflow)
        self.assertIn("GITHUB_TOKEN: ${{ github.token }}", workflow)
        self.assertIn("contents: read\n      issues: read\n      pull-requests: read\n      statuses: write", workflow)
        self.assertIn("contents: read\n      issues: write\n      pull-requests: read\n      statuses: write", workflow)
        self.assertNotIn("github.event.pull_request.head.sha", workflow)
        self.assertNotIn("download-artifact", workflow)

        checkout_steps = re.findall(
            r"(?ms)^      - name: Checkout trusted default branch\n"
            r"(?P<step>.*?)(?=^      - name:|\Z)",
            workflow,
        )
        self.assertEqual(2, len(checkout_steps))
        for step in checkout_steps:
            self.assertIn(
                "ref: ${{ github.event.repository.default_branch }}",
                step,
            )
            self.assertIn("persist-credentials: false", step)
        self.assertNotIn("download-artifact", workflow)


if __name__ == "__main__":
    main()
