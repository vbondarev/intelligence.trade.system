#!/usr/bin/env python3
"""Проверяет freshness Approved Plan из доверенного default-branch workflow."""

from __future__ import annotations

from datetime import datetime, timezone
import base64
import json
import os
from pathlib import Path
import re
import runpy
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote
from urllib.request import Request, urlopen


VALIDATOR = runpy.run_path(
    str(Path(__file__).with_name("check-pr-approved-plan.py"))
)
AMENDMENT_MARKER = VALIDATOR["AMENDMENT_MARKER"]
BASE_PLAN_MARKER = VALIDATOR["BASE_PLAN_MARKER"]
INCIDENT_MARKER = VALIDATOR["INCIDENT_MARKER"]
INTEGRITY_INCIDENT_AUTHOR = VALIDATOR["INTEGRITY_INCIDENT_AUTHOR"]
TRUSTED_PLAN_AUTHOR = VALIDATOR["TRUSTED_PLAN_AUTHOR"]
ValidationError = VALIDATOR["ValidationError"]
fetch_issue_comments = VALIDATOR["fetch_issue_comments"]
linked_issue_number = VALIDATOR["linked_issue_number"]
parse_issue_comment_permalink = VALIDATOR["parse_issue_comment_permalink"]
validate_current_pull_request = VALIDATOR["validate_current_pull_request"]

API_VERSION = "2022-11-28"
WORKFLOW_NAME = "Build and Test"
STATUS_CONTEXT = "plan-freshness"
MAX_SNAPSHOT_ATTEMPTS = 3
PULL_REQUEST_EVENT_BLOCK = re.compile(
    r"(?ms)^  pull_request:\s*\n(?P<block>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\Z)"
)
INLINE_EVENT_TYPES = re.compile(r"(?m)^\s{4}types:\s*\[([^\]]*)\]\s*$")


class HandlerError(Exception):
    """Ошибка обработки trusted Plan freshness event."""


def request_json(
    url: str,
    token: str,
    *,
    method: str = "GET",
    payload: dict[str, object] | None = None,
) -> object:
    data = None
    headers = {
        "Accept": "application/vnd.github+json",
        "Authorization": f"Bearer {token}",
        "User-Agent": "intelligence-trade-system-plan-freshness",
        "X-GitHub-Api-Version": API_VERSION,
    }
    if payload is not None:
        data = json.dumps(payload, separators=(",", ":")).encode("utf-8")
        headers["Content-Type"] = "application/json"
    request = Request(url, data=data, headers=headers, method=method)
    try:
        with urlopen(request, timeout=30) as response:
            content = response.read()
            if not content:
                return {}
            return json.loads(content.decode("utf-8"))
    except HTTPError as exception:
        raise HandlerError(f"GitHub API вернул HTTP {exception.code}.") from None
    except URLError as exception:
        raise HandlerError(
            f"Не удалось выполнить запрос к GitHub API: {exception.reason}."
        ) from None
    except (json.JSONDecodeError, UnicodeDecodeError) as exception:
        raise HandlerError(
            f"GitHub API вернул некорректный JSON: {exception}."
        ) from None
    except OSError as exception:
        raise HandlerError(f"Ошибка запроса к GitHub API: {exception}.") from None


def api_url(repository: str, path: str) -> str:
    return f"https://api.github.com/repos/{quote(repository, safe='/')}/{path.lstrip('/')}"


def get_object(
    repository: str,
    path: str,
    token: str,
) -> dict[str, object]:
    response = request_json(api_url(repository, path), token)
    if not isinstance(response, dict):
        raise HandlerError("GitHub API вернул объект некорректного типа.")
    return response


def open_pull_request_head(
    repository: str,
    pull_request_number: int,
    token: str,
) -> str | None:
    pull_request = get_object(repository, f"pulls/{pull_request_number}", token)
    if pull_request.get("state") != "open":
        return None
    base = pull_request.get("base")
    head = pull_request.get("head")
    if (
        not isinstance(base, dict)
        or not isinstance(base.get("repo"), dict)
        or not isinstance(base["repo"].get("full_name"), str)
        or base["repo"]["full_name"].casefold() != repository.casefold()
        or not isinstance(head, dict)
        or not isinstance(head.get("sha"), str)
    ):
        raise HandlerError("GitHub API вернул неполные PR head данные.")
    return head["sha"]


def write_status(
    repository: str,
    head_sha: str,
    state: str,
    description: str,
    token: str,
) -> None:
    if state not in {"pending", "success", "failure"}:
        raise HandlerError(f"Недопустимое состояние commit status: {state}.")
    response = request_json(
        api_url(repository, f"statuses/{quote(head_sha, safe='')}"),
        token,
        method="POST",
        payload={
            "state": state,
            "context": STATUS_CONTEXT,
            "description": description[:140],
        },
    )
    if not isinstance(response, dict):
        raise HandlerError("GitHub API не подтвердил запись commit status.")
    if (
        response.get("sha") != head_sha
        or response.get("context") != STATUS_CONTEXT
        or response.get("state") != state
    ):
        raise HandlerError("GitHub API записал неожиданный commit status.")


def issue_comment_author(comment: dict[str, object]) -> tuple[str, str]:
    user = comment.get("user")
    if not isinstance(user, dict):
        return "", ""
    login = user.get("login")
    association = comment.get("author_association")
    return (
        login if isinstance(login, str) else "",
        association if isinstance(association, str) else "",
    )


def pull_request_edit_event_is_enabled(workflow_text: str) -> bool:
    event_match = PULL_REQUEST_EVENT_BLOCK.search(workflow_text)
    if event_match is None:
        return False
    types_match = INLINE_EVENT_TYPES.search(event_match.group("block"))
    if types_match is None:
        return False
    event_types = {
        item.strip().strip("'\"")
        for item in types_match.group(1).split(",")
        if item.strip()
    }
    return "edited" in event_types


def ensure_pull_request_edit_event(
    repository: str,
    head_sha: str,
    token: str,
) -> None:
    ref = quote(head_sha, safe="")
    response = get_object(
        repository,
        f"contents/.github/workflows/build.yml?ref={ref}",
        token,
    )
    if response.get("encoding") != "base64" or not isinstance(
        response.get("content"),
        str,
    ):
        raise HandlerError(
            "Не удалось получить build.yml из текущего PR head."
        )
    try:
        workflow_text = base64.b64decode(response["content"]).decode("utf-8")
    except (ValueError, UnicodeDecodeError) as exception:
        raise HandlerError(
            f"Не удалось декодировать build.yml из PR head: {exception}."
        ) from None
    if not pull_request_edit_event_is_enabled(workflow_text):
        raise HandlerError(
            "PR workflow должен запускаться при pull_request edited."
        )


def comment_permalink(
    comment: dict[str, object],
    repository: str,
    issue_number: int,
) -> str:
    permalink = comment.get("html_url")
    if not isinstance(permalink, str):
        raise HandlerError("У Issue comment отсутствует permalink.")
    parse_issue_comment_permalink(
        permalink,
        repository,
        issue_number,
        "Issue comment",
    )
    return permalink


def body_marker(body: object) -> str:
    if not isinstance(body, str):
        return ""
    lines = body.splitlines()
    return lines[0] if lines else ""


def changed_body_from(event: dict[str, object]) -> str:
    changes = event.get("changes")
    if not isinstance(changes, dict):
        return ""
    body_change = changes.get("body")
    if not isinstance(body_change, dict):
        return ""
    previous_body = body_change.get("from")
    return previous_body if isinstance(previous_body, str) else ""


def classify_issue_comment_event(
    event: dict[str, object],
    repository: str,
) -> dict[str, object] | None:
    issue = event.get("issue")
    comment = event.get("comment")
    action = event.get("action")
    if not isinstance(issue, dict) or "pull_request" in issue:
        return None
    issue_number = issue.get("number")
    if not isinstance(issue_number, int) or issue_number < 1:
        raise HandlerError("issue_comment event не содержит корректный Issue number.")
    if not isinstance(comment, dict) or not isinstance(action, str):
        raise HandlerError("issue_comment event не содержит comment/action.")

    # comment.user — автор artifact; sender — actor события.
    login, association = issue_comment_author(comment)
    marker = body_marker(comment.get("body"))
    previous_marker = body_marker(changed_body_from(event))

    def artifact_reference() -> tuple[int, str]:
        current_comment_id = comment.get("id")
        if not isinstance(current_comment_id, int) or current_comment_id < 1:
            raise HandlerError(
                "Trusted canonical comment event не содержит корректный comment id."
            )
        return (
            current_comment_id,
            comment_permalink(comment, repository, issue_number),
        )

    if action == "created":
        if (
            login.casefold() == TRUSTED_PLAN_AUTHOR
            and association == "OWNER"
            and marker in {BASE_PLAN_MARKER, AMENDMENT_MARKER}
        ):
            current_comment_id, comment_url = artifact_reference()
            return {
                "issue_number": issue_number,
                "comment_id": current_comment_id,
                "comment_url": comment_url,
                "artifact_type": (
                    "base-plan" if marker == BASE_PLAN_MARKER else "amendment"
                ),
                "kind": "plan-created",
            }
        if login.casefold() == INTEGRITY_INCIDENT_AUTHOR and marker == INCIDENT_MARKER:
            current_comment_id, comment_url = artifact_reference()
            return {
                "issue_number": issue_number,
                "comment_id": current_comment_id,
                "comment_url": comment_url,
                "kind": "incident-created",
            }
        return None

    if action not in {"edited", "deleted"}:
        return None

    if login.casefold() == INTEGRITY_INCIDENT_AUTHOR and (
        marker == INCIDENT_MARKER or previous_marker == INCIDENT_MARKER
    ):
        return {
            "issue_number": issue_number,
            "comment_id": comment.get("id"),
            "comment_url": comment.get("html_url"),
            "kind": "incident-mutated",
        }

    if login.casefold() != TRUSTED_PLAN_AUTHOR:
        return None
    if association != "OWNER":
        if marker in {BASE_PLAN_MARKER, AMENDMENT_MARKER} or previous_marker in {
            BASE_PLAN_MARKER,
            AMENDMENT_MARKER,
        }:
            raise HandlerError(
                "Не удалось подтвердить OWNER-authority исходного Plan comment."
            )
        return None

    if action == "deleted" and not isinstance(comment.get("body"), str):
        raise HandlerError(
            "Deleted comment payload не содержит исходный body для проверки trust."
        )
    if (
        action == "edited"
        and not changed_body_from(event)
        and marker not in {BASE_PLAN_MARKER, AMENDMENT_MARKER}
    ):
        raise HandlerError(
            "Edited comment payload не позволяет подтвердить предыдущий artifact."
        )

    canonical_marker = (
        previous_marker
        if previous_marker in {BASE_PLAN_MARKER, AMENDMENT_MARKER}
        else marker
    )
    if canonical_marker not in {BASE_PLAN_MARKER, AMENDMENT_MARKER}:
        return None
    current_comment_id, comment_url = artifact_reference()

    return {
        "issue_number": issue_number,
        "comment_id": current_comment_id,
        "comment_url": comment_url,
        "artifact_type": (
            "base-plan" if canonical_marker == BASE_PLAN_MARKER else "amendment"
        ),
        "kind": f"plan-{action}",
    }


def incident_fields(comment: dict[str, object]) -> dict[str, str]:
    body = comment.get("body")
    if not isinstance(body, str) or body_marker(body) != INCIDENT_MARKER:
        return {}
    fields: dict[str, str] = {}
    for line in body.splitlines():
        if ": " not in line:
            continue
        name, value = line.split(": ", 1)
        if name in {
            "Affected comment",
            "Artifact type",
            "Action",
            "Recorded at",
        }:
            if name in fields:
                raise HandlerError(f"В Integrity Incident повторяется поле '{name}'.")
            fields[name] = value
    return fields


def create_integrity_incident(
    repository: str,
    change: dict[str, object],
    token: str,
) -> dict[str, object]:
    issue_number = change["issue_number"]
    comment_url = change["comment_url"]
    artifact_type = change["artifact_type"]
    action = change["kind"].removeprefix("plan-")
    if not isinstance(issue_number, int) or not isinstance(comment_url, str):
        raise HandlerError("Integrity Incident event metadata некорректны.")
    if artifact_type not in {"base-plan", "amendment"} or action not in {
        "edited",
        "deleted",
    }:
        raise HandlerError("Integrity Incident event имеет недопустимый тип.")

    existing = validator_comments(repository, issue_number, token)
    for comment in existing:
        if (
            issue_comment_author(comment)[0].casefold()
            == INTEGRITY_INCIDENT_AUTHOR
            and body_marker(comment.get("body")) == INCIDENT_MARKER
        ):
            fields = incident_fields(comment)
            if fields.get("Affected comment") != comment_url:
                continue
            if fields.get("Artifact type") != artifact_type:
                raise HandlerError(
                    "Для invalidated comment уже есть Integrity Incident другого типа."
                )
            if (
                set(fields)
                != {"Affected comment", "Artifact type", "Action", "Recorded at"}
                or fields.get("Action") not in {"edited", "deleted"}
                or comment.get("created_at") != comment.get("updated_at")
            ):
                raise HandlerError(
                    "Существующий Integrity Incident повреждён или изменён."
                )
            return comment

    recorded_at = (
        datetime.now(timezone.utc)
        .isoformat(timespec="seconds")
        .replace("+00:00", "Z")
    )
    body = (
        f"{INCIDENT_MARKER}\n"
        f"Affected comment: {comment_url}\n"
        f"Artifact type: {artifact_type}\n"
        f"Action: {action}\n"
        f"Recorded at: {recorded_at}\n"
    )
    created = request_json(
        api_url(repository, f"issues/{issue_number}/comments"),
        token,
        method="POST",
        payload={"body": body},
    )
    if not isinstance(created, dict):
        raise HandlerError("GitHub API не вернул созданный Integrity Incident.")
    comment_id = created.get("id")
    if not isinstance(comment_id, int):
        raise HandlerError("Созданный Integrity Incident не содержит id.")

    stored = get_object(repository, f"issues/comments/{comment_id}", token)
    if (
        comment_body(stored) != body
        or issue_comment_author(stored)[0].casefold() != INTEGRITY_INCIDENT_AUTHOR
        or stored.get("created_at") != stored.get("updated_at")
    ):
        raise HandlerError("Не удалось подтвердить durable Integrity Incident.")
    return stored


def comment_body(comment: dict[str, object]) -> str:
    body = comment.get("body")
    return body if isinstance(body, str) else ""


def validator_comments(
    repository: str,
    issue_number: int,
    token: str,
) -> list[dict[str, object]]:
    return fetch_issue_comments(repository, issue_number, token)


def list_open_pull_requests(
    repository: str,
    token: str,
) -> list[dict[str, object]]:
    pull_requests: list[dict[str, object]] = []
    page = 1
    while True:
        response = request_json(
            api_url(
                repository,
                f"pulls?state=open&per_page=100&page={page}",
            ),
            token,
        )
        if not isinstance(response, list) or any(
            not isinstance(pull_request, dict) for pull_request in response
        ):
            raise HandlerError("GitHub API вернул некорректный список Pull Requests.")
        pull_requests.extend(response)
        if len(response) < 100:
            return pull_requests
        page += 1


def linked_issue_from_pr(
    pull_request: dict[str, object],
) -> int | None:
    body = pull_request.get("body")
    if not isinstance(body, str):
        return None
    try:
        return linked_issue_number(body)
    except ValidationError:
        return None


def find_open_linked_pull_requests(
    repository: str,
    issue_number: int,
    token: str,
) -> list[dict[str, object]]:
    return [
        pull_request
        for pull_request in list_open_pull_requests(repository, token)
        if linked_issue_from_pr(pull_request) == issue_number
    ]


def workflow_run_targets(
    repository: str,
    workflow_run: dict[str, object],
    token: str,
) -> list[tuple[int, str]]:
    if workflow_run.get("name") != WORKFLOW_NAME:
        return []
    if workflow_run.get("path") not in {
        None,
        ".github/workflows/build.yml",
    }:
        return []
    if workflow_run.get("event") != "pull_request":
        return []

    associated_prs = workflow_run.get("pull_requests")
    if not isinstance(associated_prs, list) or not associated_prs:
        run_id = workflow_run.get("id")
        if not isinstance(run_id, int):
            raise HandlerError("workflow_run event не содержит run id.")
        details = get_object(repository, f"actions/runs/{run_id}", token)
        associated_prs = details.get("pull_requests")
    if not isinstance(associated_prs, list):
        raise HandlerError("Не удалось получить PR для workflow_run.")

    targets: list[tuple[int, str]] = []
    for associated_pr in associated_prs:
        if not isinstance(associated_pr, dict):
            continue
        number = associated_pr.get("number")
        head = associated_pr.get("head")
        if (
            not isinstance(number, int)
            or not isinstance(head, dict)
            or not isinstance(head.get("sha"), str)
        ):
            continue
        event_sha = head["sha"]
        current_sha = open_pull_request_head(repository, number, token)
        if current_sha is None:
            continue
        if current_sha == event_sha:
            targets.append((number, event_sha))
    return targets


def linked_pull_request_targets(
    repository: str,
    issue_number: int,
    token: str,
) -> list[tuple[int, str]]:
    targets: list[tuple[int, str]] = []
    for pull_request in find_open_linked_pull_requests(
        repository,
        issue_number,
        token,
    ):
        number = pull_request.get("number")
        if not isinstance(number, int):
            continue
        head_sha = open_pull_request_head(repository, number, token)
        if head_sha is not None:
            targets.append((number, head_sha))
    return targets


def stable_validation(
    repository: str,
    pull_request_number: int,
    expected_head_sha: str,
    token: str,
) -> object:
    for _ in range(MAX_SNAPSHOT_ATTEMPTS):
        first = validate_current_pull_request(
            repository,
            pull_request_number,
            expected_head_sha=expected_head_sha,
            token=token,
        )
        second = validate_current_pull_request(
            repository,
            pull_request_number,
            expected_head_sha=expected_head_sha,
            token=token,
        )
        if first.snapshot_sha256 == second.snapshot_sha256:
            return second
    raise HandlerError(
        "PR/Issue metadata изменялись во время validation; freshness не подтверждена."
    )


def publish_validation_result(
    repository: str,
    pull_request_number: int,
    expected_head_sha: str,
    token: str,
) -> None:
    for _attempt in range(MAX_SNAPSHOT_ATTEMPTS):
        try:
            ensure_pull_request_edit_event(
                repository,
                expected_head_sha,
                token,
            )
            outcome = stable_validation(
                repository,
                pull_request_number,
                expected_head_sha,
                token,
            )
        except (ValidationError, HandlerError) as exception:
            write_status(
                repository,
                expected_head_sha,
                "failure",
                f"Проверка Plan не пройдена: {exception}",
                token,
            )
            return

        write_status(
            repository,
            expected_head_sha,
            "success",
            f"Approved Plan проверен; snapshot {outcome.snapshot_sha256[:16]}.",
            token,
        )
        try:
            ensure_pull_request_edit_event(
                repository,
                expected_head_sha,
                token,
            )
            latest = validate_current_pull_request(
                repository,
                pull_request_number,
                expected_head_sha=expected_head_sha,
                token=token,
            )
        except (ValidationError, HandlerError) as exception:
            write_status(
                repository,
                expected_head_sha,
                "failure",
                f"Проверка Plan не пройдена: {exception}",
                token,
            )
            return
        if latest.snapshot_sha256 == outcome.snapshot_sha256:
            return
        write_status(
            repository,
            expected_head_sha,
            "pending",
            "Snapshot изменился; повторная Plan validation.",
            token,
        )

    write_status(
        repository,
        expected_head_sha,
        "failure",
        "PR/Issue metadata изменялись во время validation.",
        token,
    )


def set_pending_for_targets(
    repository: str,
    targets: list[tuple[int, str]],
    token: str,
) -> None:
    for _, head_sha in targets:
        write_status(
            repository,
            head_sha,
            "pending",
            "Ожидает повторной проверки Approved Plan.",
            token,
        )


def handle_workflow_run_event(
    event: dict[str, object],
    repository: str,
    token: str,
) -> str:
    workflow_run = event.get("workflow_run")
    action = event.get("action")
    if not isinstance(workflow_run, dict) or action not in {
        "requested",
        "completed",
    }:
        raise HandlerError("Некорректный workflow_run event.")
    targets = workflow_run_targets(repository, workflow_run, token)
    if not targets:
        return "Workflow run не относится к актуальному открытому PR."

    set_pending_for_targets(repository, targets, token)
    if action == "requested":
        return f"plan-freshness переведён в pending для {len(targets)} PR."

    for pull_request_number, head_sha in targets:
        publish_validation_result(
            repository,
            pull_request_number,
            head_sha,
            token,
        )
    return f"plan-freshness обновлён для {len(targets)} PR."


def handle_issue_comment_event(
    event: dict[str, object],
    repository: str,
    token: str,
) -> str:
    try:
        change = classify_issue_comment_event(event, repository)
    except HandlerError as exception:
        issue = event.get("issue")
        issue_number = issue.get("number") if isinstance(issue, dict) else None
        if (
            isinstance(issue, dict)
            and "pull_request" not in issue
            and isinstance(issue_number, int)
            and issue_number > 0
        ):
            targets = linked_pull_request_targets(
                repository,
                issue_number,
                token,
            )
            set_pending_statuses(repository, targets, token)
            fail_statuses(
                repository,
                targets,
                f"Не удалось проверить Issue comment event: {exception}",
                token,
            )
        raise
    if change is None:
        return "Issue comment не относится к trusted Plan workflow."

    issue_number = change["issue_number"]
    if not isinstance(issue_number, int):
        raise HandlerError("issue_comment event имеет некорректный Issue number.")
    targets = linked_pull_request_targets(repository, issue_number, token)
    kind = change["kind"]
    try:
        set_pending_for_targets(repository, targets, token)
    except HandlerError:
        if kind in {"plan-edited", "plan-deleted"}:
            create_integrity_incident(repository, change, token)
        raise

    if kind in {"plan-edited", "plan-deleted"}:
        try:
            create_integrity_incident(repository, change, token)
        except HandlerError as exception:
            for pull_request_number, head_sha in targets:
                write_status(
                    repository,
                    head_sha,
                    "failure",
                    f"Integrity Incident не записан: {exception}",
                    token,
                )
            raise
    elif kind == "incident-mutated":
        for pull_request_number, head_sha in targets:
            write_status(
                repository,
                head_sha,
                "failure",
                "Integrity Incident был изменён или удалён.",
                token,
            )
        raise HandlerError("Integrity Incident был изменён или удалён.")

    for pull_request_number, head_sha in targets:
        publish_validation_result(
            repository,
            pull_request_number,
            head_sha,
            token,
        )
    return f"Issue comment обработан; проверено PR: {len(targets)}."


def main() -> int:
    event_path_value = os.environ.get("GITHUB_EVENT_PATH")
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    token = os.environ.get("GITHUB_TOKEN", "")
    event_name = os.environ.get("GITHUB_EVENT_NAME", "")
    if not event_path_value or not Path(event_path_value).is_file():
        print("GITHUB_EVENT_PATH не указывает на event payload.", file=sys.stderr)
        return 1
    if not repository or len(repository.split("/")) != 2:
        print("GITHUB_REPOSITORY должен иметь формат <owner>/<repo>.", file=sys.stderr)
        return 1
    if not token:
        print("Для trusted Plan freshness workflow требуется GITHUB_TOKEN.", file=sys.stderr)
        return 1

    try:
        event = json.loads(Path(event_path_value).read_text(encoding="utf-8"))
        if not isinstance(event, dict):
            raise HandlerError("GitHub event payload должен быть JSON object.")
        if event_name == "workflow_run":
            result = handle_workflow_run_event(event, repository, token)
        elif event_name == "issue_comment":
            result = handle_issue_comment_event(event, repository, token)
        else:
            raise HandlerError(f"Event {event_name} не поддерживается.")
    except (OSError, json.JSONDecodeError, HandlerError) as exception:
        print(f"Trusted Plan freshness не пройдена: {exception}", file=sys.stderr)
        return 1

    print(f"Trusted Plan freshness обработана: {result}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
