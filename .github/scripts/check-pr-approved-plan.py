#!/usr/bin/env python3
"""Проверяет связи PR с Issue, Approved Plan и Amendments."""

from __future__ import annotations

from dataclasses import dataclass
from datetime import datetime
import hashlib
import json
import os
from pathlib import Path
import re
import sys
from urllib.error import HTTPError, URLError
from urllib.parse import quote, urlsplit
from urllib.request import Request, urlopen


BASE_PLAN_MARKER = "# Approved Implementation Plan"
AMENDMENT_MARKER = "# Approved Implementation Plan — Amendment"
INCIDENT_MARKER = "# Approved Plan Integrity Incident"
BASE_PLAN_FIELD = "Approved Implementation Plan:"
AMENDMENTS_FIELD = "Approved Plan Amendments:"
BASE_AMENDMENT_LINK_FIELD = "Base Approved Implementation Plan:"
INCIDENT_SUPERSEDES_FIELD = "Supersedes Approved Plan Integrity Incident:"
TRUSTED_PLAN_AUTHOR = "vbondarev"
INTEGRITY_INCIDENT_AUTHOR = "github-actions[bot]"
ISSUE_CLOSE_LINE = re.compile(r"^\s*Closes\s+#([1-9][0-9]*)\s*$", re.IGNORECASE)
ISSUE_CLOSE_START = re.compile(r"^\s*Closes\b", re.IGNORECASE)
PLAN_FIELD_LINE = re.compile(r"^\s*Approved Implementation Plan:\s*(.*?)\s*$")
BASE_AMENDMENT_LINK_LINE = re.compile(
    r"^\s*Base Approved Implementation Plan:\s*(.*?)\s*$"
)
BASE_AMENDMENT_LINK_LINE = re.compile(
    r"^\s*Base Approved Implementation Plan:\s*(.*?)\s*$"
)
INCIDENT_FIELD_LINE = re.compile(
    r"^\s*(Affected comment|Artifact type|Action|Recorded at):\s*(.*?)\s*$"
)
INCIDENT_SUPERSEDES_LINE = re.compile(
    r"^\s*Supersedes Approved Plan Integrity Incident:\s*(.*?)\s*$"
)
COMMENT_ID_FRAGMENT = re.compile(r"^issuecomment-([1-9][0-9]*)$")
API_VERSION = "2022-11-28"


class ValidationError(Exception):
    """Ошибка проверки PR workflow metadata."""


@dataclass(frozen=True, slots=True)
class PullRequestValidation:
    message: str
    issue_number: int
    pull_request_number: int
    head_sha: str
    snapshot_sha256: str


def linked_issue_number(body: str) -> int:
    close_lines = [
        line for line in body.splitlines() if ISSUE_CLOSE_START.match(line)
    ]
    if len(close_lines) != 1:
        raise ValidationError(
            "В PR body должна быть ровно одна primary-ссылка вида 'Closes #<issue>'."
        )

    match = ISSUE_CLOSE_LINE.fullmatch(close_lines[0])
    if match is None:
        raise ValidationError(
            "Primary Issue link должна иметь точный вид 'Closes #<issue>'."
        )
    return int(match.group(1))


def parse_issue_comment_permalink(
    permalink: str,
    repository: str,
    expected_issue: int,
    field_name: str,
) -> int:
    try:
        parsed = urlsplit(permalink)
    except ValueError as exception:
        raise ValidationError(f"{field_name}: некорректный permalink.") from exception
    repository_parts = repository.split("/")
    if (
        parsed.scheme != "https"
        or parsed.netloc.casefold() != "github.com"
        or len(repository_parts) != 2
        or parsed.query
        or parsed.username is not None
        or parsed.password is not None
        or parsed.port is not None
    ):
        raise ValidationError(f"{field_name}: укажите permalink GitHub Issue comment.")

    path_parts = parsed.path.strip("/").split("/")
    if (
        len(path_parts) != 4
        or path_parts[2].casefold() != "issues"
        or re.fullmatch(r"[0-9]+", path_parts[3]) is None
    ):
        raise ValidationError(
            f"{field_name}: ссылка должна вести на GitHub Issue comment."
        )

    linked_repository = f"{path_parts[0]}/{path_parts[1]}"
    if linked_repository.casefold() != repository.casefold():
        raise ValidationError(f"{field_name}: ссылка ведёт в другой repository.")
    if int(path_parts[3]) != expected_issue:
        raise ValidationError(f"{field_name}: ссылка ведёт на другую Issue.")

    comment_match = COMMENT_ID_FRAGMENT.fullmatch(parsed.fragment)
    if comment_match is None:
        raise ValidationError(
            f"{field_name}: permalink должен содержать fragment #issuecomment-<id>."
        )
    return int(comment_match.group(1))


def plan_field_value(body: str) -> str:
    values = [
        match.group(1)
        for line in body.splitlines()
        if (match := PLAN_FIELD_LINE.fullmatch(line)) is not None
    ]
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            "В PR body должно быть ровно одно непустое поле "
            "'Approved Implementation Plan:'."
        )
    return values[0]


def amendment_values(body: str) -> list[str]:
    lines = body.splitlines()
    section_indices = [
        index for index, line in enumerate(lines) if line.strip() == AMENDMENTS_FIELD
    ]
    if len(section_indices) != 1:
        raise ValidationError(
            "В PR body должен быть ровно один раздел 'Approved Plan Amendments:'."
        )

    section_start = section_indices[0] + 1
    section_end = next(
        (
            index
            for index in range(section_start, len(lines))
            if lines[index].startswith("## ")
        ),
        len(lines),
    )
    values: list[str] = []
    for line in lines[section_start:section_end]:
        if not line.strip():
            continue
        if not line.startswith("- ") or not line[2:].strip():
            raise ValidationError(
                "Approved Plan Amendments нужно указывать как '- <permalink>' "
                "или '- Нет'."
            )
        values.append(line[2:].strip())

    if not values:
        raise ValidationError(
            "В Approved Plan Amendments укажите permalinks или '- Нет'."
        )
    if "Нет" in values and values != ["Нет"]:
        raise ValidationError(
            "'- Нет' нельзя указывать вместе со ссылками на amendments."
        )
    return values


def comment_body(comment: dict[str, object]) -> str:
    body = comment.get("body")
    return body if isinstance(body, str) else ""


def comment_marker(comment: dict[str, object]) -> str:
    lines = comment_body(comment).splitlines()
    return lines[0] if lines else ""


def comment_id(comment: dict[str, object]) -> str:
    value = comment.get("id")
    return str(value) if isinstance(value, (int, str)) else ""


def comment_author(comment: dict[str, object]) -> tuple[str, str]:
    user = comment.get("user")
    if not isinstance(user, dict):
        return "", ""
    login = user.get("login")
    association = comment.get("author_association")
    return (
        login if isinstance(login, str) else "",
        association if isinstance(association, str) else "",
    )


def is_trusted_plan_author(comment: dict[str, object]) -> bool:
    login, association = comment_author(comment)
    return login.casefold() == TRUSTED_PLAN_AUTHOR and association == "OWNER"


def is_integrity_incident_author(comment: dict[str, object]) -> bool:
    login, _ = comment_author(comment)
    return login.casefold() == INTEGRITY_INCIDENT_AUTHOR


def comment_is_immutable(comment: dict[str, object], role: str) -> None:
    created_at = comment.get("created_at")
    updated_at = comment.get("updated_at")
    if (
        not isinstance(created_at, str)
        or not created_at
        or not isinstance(updated_at, str)
        or not updated_at
    ):
        raise ValidationError(f"У {role} отсутствуют created_at/updated_at.")
    if created_at != updated_at:
        raise ValidationError(f"{role} был изменён после публикации.")


def parse_timestamp(value: object, description: str) -> datetime:
    if not isinstance(value, str) or not value:
        raise ValidationError(f"У {description} отсутствует timestamp.")
    try:
        timestamp = datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError as exception:
        raise ValidationError(
            f"У {description} некорректный timestamp."
        ) from exception
    if timestamp.tzinfo is None:
        raise ValidationError(f"У {description} timestamp должен содержать timezone.")
    return timestamp


def one_field_value(body: str, pattern: re.Pattern[str], field: str) -> str:
    values = [
        match.group(1)
        for line in body.splitlines()
        if (match := pattern.fullmatch(line)) is not None
    ]
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            f"В Integrity Incident должно быть ровно одно поле '{field}'."
        )
    return values[0]


def parse_integrity_incidents(
    comments: list[dict[str, object]],
    repository: str,
    issue_number: int,
) -> dict[int, dict[str, object]]:
    incidents: dict[int, dict[str, object]] = {}
    for comment in comments:
        if comment_marker(comment) != INCIDENT_MARKER:
            continue
        if not is_integrity_incident_author(comment):
            continue
        comment_is_immutable(comment, "Integrity Incident")
        body = comment_body(comment)
        fields: dict[str, str] = {}
        for line in body.splitlines():
            match = INCIDENT_FIELD_LINE.fullmatch(line)
            if match is None:
                continue
            name, value = match.groups()
            if name in fields:
                raise ValidationError(
                    f"В Integrity Incident повторяется поле '{name}'."
                )
            fields[name] = value

        required_fields = {
            "Affected comment",
            "Artifact type",
            "Action",
            "Recorded at",
        }
        if set(fields) != required_fields:
            raise ValidationError(
                "Integrity Incident должен содержать только один экземпляр "
                "каждого обязательного поля."
            )
        artifact_type = fields["Artifact type"]
        action = fields["Action"]
        if artifact_type not in {"base-plan", "amendment"}:
            raise ValidationError("У Integrity Incident неизвестный artifact type.")
        if action not in {"edited", "deleted"}:
            raise ValidationError("У Integrity Incident неизвестный action.")

        affected_id = parse_issue_comment_permalink(
            fields["Affected comment"],
            repository,
            issue_number,
            "Affected comment",
        )
        incident_timestamp = parse_timestamp(fields["Recorded at"], "Integrity Incident")
        comment_timestamp = parse_timestamp(
            comment.get("created_at"),
            "Integrity Incident comment",
        )
        if abs((incident_timestamp - comment_timestamp).total_seconds()) > 300:
            raise ValidationError(
                "Integrity Incident timestamp не совпадает со временем comment."
            )
        if affected_id in incidents:
            raise ValidationError(
                "Для одного invalidated comment найдено несколько Integrity Incidents."
            )
        incidents[affected_id] = {
            "comment": comment,
            "permalink": comment.get("html_url"),
            "artifact_type": artifact_type,
            "timestamp": incident_timestamp,
        }
    return incidents


def replacement_incident_permalink(comment: dict[str, object]) -> str | None:
    values = [
        match.group(1)
        for line in comment_body(comment).splitlines()
        if (match := INCIDENT_SUPERSEDES_LINE.fullmatch(line)) is not None
    ]
    if not values:
        return None
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            "Canonical replacement должен содержать ровно одну ссылку "
            "Supersedes Approved Plan Integrity Incident."
        )
    return values[0]


def validate_effective_artifacts(
    issue_comments: list[dict[str, object]],
    repository: str,
    issue_number: int,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    incidents = parse_integrity_incidents(issue_comments, repository, issue_number)
    incident_by_permalink: dict[str, tuple[int, dict[str, object]]] = {}
    for target_id, incident in incidents.items():
        permalink = incident.get("permalink")
        if not isinstance(permalink, str):
            raise ValidationError("У Integrity Incident отсутствует html_url.")
        incident_by_permalink[permalink] = (target_id, incident)

    active_base_comments: list[dict[str, object]] = []
    active_amendments: list[dict[str, object]] = []
    replacement_by_incident: dict[int, dict[str, object]] = {}

    for comment in issue_comments:
        marker = comment_marker(comment)
        if marker not in {BASE_PLAN_MARKER, AMENDMENT_MARKER}:
            continue
        if not is_trusted_plan_author(comment):
            continue

        target_id_text = comment_id(comment)
        if not target_id_text.isdigit():
            raise ValidationError("У canonical Plan comment отсутствует корректный id.")
        target_id = int(target_id_text)
        artifact_type = "base-plan" if marker == BASE_PLAN_MARKER else "amendment"
        incident = incidents.get(target_id)
        if incident is not None and incident["artifact_type"] != artifact_type:
            raise ValidationError(
                "Artifact type Integrity Incident не совпадает с canonical comment."
            )

        supersedes_permalink = replacement_incident_permalink(comment)
        if supersedes_permalink is not None:
            incident_match = incident_by_permalink.get(supersedes_permalink)
            if incident_match is None:
                raise ValidationError(
                    "Canonical replacement ссылается на отсутствующий "
                    "Integrity Incident."
                )
            incident_id, incident_info = incident_match
            if incident_info["artifact_type"] != artifact_type:
                raise ValidationError(
                    "Artifact type replacement не совпадает с Integrity Incident."
                )
            if incident_id in replacement_by_incident:
                raise ValidationError(
                    "Для Integrity Incident найдено несколько canonical replacements."
                )
            comment_is_immutable(comment, "Canonical replacement")
            replacement_created = parse_timestamp(
                comment.get("created_at"),
                "Canonical replacement",
            )
            if replacement_created <= incident_info["timestamp"]:
                raise ValidationError(
                    "Canonical replacement должен быть создан после Integrity Incident."
                )
            replacement_by_incident[incident_id] = comment
        else:
            if target_id in incidents:
                continue
            comment_is_immutable(comment, "Canonical Plan")

        if target_id in incidents:
            continue
        if marker == BASE_PLAN_MARKER:
            active_base_comments.append(comment)
        else:
            active_amendments.append(comment)

    for target_id in incidents:
        if target_id not in replacement_by_incident:
            raise ValidationError(
                "Invalidated Plan artifact не имеет утверждённого canonical replacement."
            )

    return active_base_comments, active_amendments


def comment_url(comment: dict[str, object]) -> str:
    url = comment.get("html_url")
    if not isinstance(url, str) or not url:
        raise ValidationError("У Issue comment отсутствует html_url.")
    return url


def amendment_base_permalink(comment: dict[str, object]) -> str:
    values = [
        match.group(1)
        for line in comment_body(comment).splitlines()
        if (match := BASE_AMENDMENT_LINK_LINE.fullmatch(line)) is not None
    ]
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            "Каждый Approved Plan Amendment должен содержать ровно один "
            "permalink в поле 'Base Approved Implementation Plan:'."
        )
    return values[0]


def issue_is_pull_request(issue: dict[str, object]) -> bool:
    return "pull_request" in issue


def validate_pull_request(
    body: str,
    repository: str,
    issue: dict[str, object],
    issue_comments: list[dict[str, object]],
) -> str:
    issue_number_value = issue.get("number")
    if not isinstance(issue_number_value, int) or issue_number_value < 1:
        raise ValidationError("GitHub API вернул Issue без корректного number.")
    issue_number = issue_number_value
    if issue_is_pull_request(issue):
        raise ValidationError("Closes должен ссылаться на Issue, а не на Pull Request.")

    linked_issue_number_value = linked_issue_number(body)
    if linked_issue_number_value != issue_number:
        raise ValidationError("Closes и загруженная Issue имеют разные номера.")

    base_permalink = plan_field_value(body)
    listed_amendment_values = amendment_values(body)
    issue_comment_map = {
        comment_id(comment): comment
        for comment in issue_comments
        if comment_id(comment)
    }
    base_comments, amendment_comments = validate_effective_artifacts(
        issue_comments,
        repository,
        issue_number,
    )
    incidents = parse_integrity_incidents(issue_comments, repository, issue_number)

    if base_permalink == "N/A":
        if base_comments or amendment_comments or incidents:
            raise ValidationError(
                "Нельзя использовать 'N/A': в Issue есть Plan, Amendment "
                "или Integrity Incident."
            )
        if listed_amendment_values != ["Нет"]:
            raise ValidationError(
                "При 'N/A' в PR в Approved Plan Amendments укажите '- Нет'."
            )
        return f"Issue #{issue_number}: Approved Plan и amendments отсутствуют."

    base_comment_id = parse_issue_comment_permalink(
        base_permalink,
        repository,
        issue_number,
        BASE_PLAN_FIELD,
    )
    if len(base_comments) != 1:
        raise ValidationError(
            "В связанной Issue должен быть ровно один active canonical Approved Plan."
        )
    base_comment = base_comments[0]
    if comment_id(base_comment) != str(base_comment_id):
        raise ValidationError(
            "Permalink Approved Implementation Plan не указывает на active "
            "canonical base Plan."
        )

    for amendment in amendment_comments:
        if amendment_base_permalink(amendment) != base_permalink:
            raise ValidationError(
                "Approved Plan Amendment ссылается не на active base Plan."
            )

    if listed_amendment_values == ["Нет"]:
        listed_amendment_ids: list[int] = []
    else:
        listed_amendment_ids = [
            parse_issue_comment_permalink(
                permalink,
                repository,
                issue_number,
                AMENDMENTS_FIELD,
            )
            for permalink in listed_amendment_values
        ]
        if len(set(listed_amendment_ids)) != len(listed_amendment_ids):
            raise ValidationError("В PR повторяется ссылка на Approved Plan Amendment.")

    canonical_amendment_ids = [
        int(comment_id(comment))
        for comment in amendment_comments
        if comment_id(comment).isdigit()
    ]
    if listed_amendment_ids != canonical_amendment_ids:
        raise ValidationError(
            "В PR перечислите каждый active canonical Approved Plan Amendment "
            "ровно один раз и в порядке comments Issue."
        )

    for amendment_id in listed_amendment_ids:
        amendment = issue_comment_map.get(str(amendment_id))
        if amendment is None or amendment not in amendment_comments:
            raise ValidationError(
                "Указанный Approved Plan Amendment отсутствует или не является trusted."
            )
        if amendment_base_permalink(amendment) != base_permalink:
            raise ValidationError(
                "Указанный Approved Plan Amendment ссылается не на active base Plan."
            )

    return (
        f"Issue #{issue_number}, Approved Plan comment #{base_comment_id}, "
        f"{len(listed_amendment_ids)} amendment(s) validated."
    )


def request_json(url: str, token: str | None = None) -> object:
    headers = {
        "Accept": "application/vnd.github+json",
        "User-Agent": "intelligence-trade-system-agent-workflow-validator",
        "X-GitHub-Api-Version": API_VERSION,
    }
    if token:
        headers["Authorization"] = f"Bearer {token}"
    request = Request(url, headers=headers)
    try:
        with urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except HTTPError as exception:
        raise ValidationError(
            f"GitHub API вернул HTTP {exception.code}."
        ) from None
    except URLError as exception:
        raise ValidationError(
            f"Не удалось выполнить запрос к GitHub API: {exception.reason}."
        ) from None
    except (json.JSONDecodeError, UnicodeDecodeError) as exception:
        raise ValidationError(
            f"GitHub API вернул некорректный JSON: {exception}."
        ) from None
    except OSError as exception:
        raise ValidationError(f"Ошибка запроса к GitHub API: {exception}.") from None


def get_api_object(url: str, token: str | None = None) -> dict[str, object]:
    response = request_json(url, token)
    if not isinstance(response, dict):
        raise ValidationError("GitHub API вернул объект некорректного типа.")
    return response


def fetch_current_pull_request(
    repository: str,
    pull_request_number: int,
    token: str | None = None,
) -> dict[str, object]:
    encoded_repository = quote(repository, safe="/")
    pull_request = get_api_object(
        f"https://api.github.com/repos/{encoded_repository}/pulls/"
        f"{pull_request_number}",
        token,
    )
    returned_number = pull_request.get("number")
    base = pull_request.get("base")
    head = pull_request.get("head")
    if (
        returned_number != pull_request_number
        or not isinstance(base, dict)
        or not isinstance(head, dict)
        or not isinstance(base.get("repo"), dict)
        or not isinstance(base["repo"].get("full_name"), str)
        or not isinstance(head.get("sha"), str)
        or not head["sha"]
        or not isinstance(pull_request.get("body"), (str, type(None)))
    ):
        raise ValidationError("GitHub API вернул неполные данные Pull Request.")
    if base["repo"]["full_name"].casefold() != repository.casefold():
        raise ValidationError("Pull Request относится к другому repository.")
    if pull_request.get("state") != "open":
        raise ValidationError("Связанный Pull Request больше не открыт.")
    return pull_request


def fetch_issue(
    repository: str,
    issue_number: int,
    token: str | None = None,
) -> dict[str, object]:
    encoded_repository = quote(repository, safe="/")
    return get_api_object(
        f"https://api.github.com/repos/{encoded_repository}/issues/{issue_number}",
        token,
    )


def fetch_issue_comments(
    repository: str,
    issue_number: int,
    token: str | None = None,
) -> list[dict[str, object]]:
    comments: list[dict[str, object]] = []
    page = 1
    encoded_repository = quote(repository, safe="/")
    while True:
        url = (
            f"https://api.github.com/repos/{encoded_repository}/issues/"
            f"{issue_number}/comments?per_page=100&page={page}"
        )
        response = request_json(url, token)
        if not isinstance(response, list) or any(
            not isinstance(comment, dict) for comment in response
        ):
            raise ValidationError("GitHub API вернул некорректный список Issue comments.")
        comments.extend(response)
        if len(response) < 100:
            return comments
        page += 1


def validation_snapshot(
    pull_request: dict[str, object],
    issue_number: int,
    issue_comments: list[dict[str, object]],
) -> str:
    head = pull_request.get("head")
    if not isinstance(head, dict):
        raise ValidationError("У Pull Request отсутствует head.")
    head_sha = head.get("sha")
    if not isinstance(head_sha, str):
        raise ValidationError("У Pull Request отсутствует head SHA.")

    relevant_comments: list[dict[str, object]] = []
    for comment in issue_comments:
        marker = comment_marker(comment)
        login, _ = comment_author(comment)
        if is_trusted_plan_author(comment) or (
            marker == INCIDENT_MARKER
            and is_integrity_incident_author(comment)
        ):
            relevant_comments.append(
                {
                    "id": comment_id(comment),
                    "body": comment_body(comment),
                    "html_url": comment.get("html_url"),
                    "author": login,
                    "author_association": comment.get("author_association"),
                    "created_at": comment.get("created_at"),
                    "updated_at": comment.get("updated_at"),
                }
            )

    snapshot = {
        "pull_request_number": pull_request.get("number"),
        "head_sha": head_sha,
        "body": pull_request.get("body"),
        "issue_number": issue_number,
        "comments": relevant_comments,
    }
    encoded = json.dumps(
        snapshot,
        ensure_ascii=False,
        sort_keys=True,
        separators=(",", ":"),
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def validate_current_pull_request(
    repository: str,
    pull_request_number: int,
    expected_head_sha: str | None = None,
    token: str | None = None,
) -> PullRequestValidation:
    pull_request = fetch_current_pull_request(
        repository,
        pull_request_number,
        token,
    )
    head = pull_request["head"]
    assert isinstance(head, dict)
    current_head_sha = head["sha"]
    assert isinstance(current_head_sha, str)
    if expected_head_sha is not None and current_head_sha != expected_head_sha:
        raise ValidationError(
            "PR head изменился после создания event; проверка этого revision остановлена."
        )

    body = pull_request.get("body")
    if not isinstance(body, str):
        body = ""
    issue_number = linked_issue_number(body)
    issue = fetch_issue(repository, issue_number, token)
    if issue_is_pull_request(issue):
        raise ValidationError("Closes ссылается на Pull Request, а не на Issue.")
    issue_comments = fetch_issue_comments(repository, issue_number, token)
    result = validate_pull_request(body, repository, issue, issue_comments)
    snapshot = validation_snapshot(pull_request, issue_number, issue_comments)
    return PullRequestValidation(
        message=result,
        issue_number=issue_number,
        pull_request_number=pull_request_number,
        head_sha=current_head_sha,
        snapshot_sha256=snapshot,
    )


def main() -> int:
    event_path_value = os.environ.get("GITHUB_EVENT_PATH")
    if not event_path_value:
        print(
            "GITHUB_EVENT_PATH должен указывать на pull request event payload.",
            file=sys.stderr,
        )
        return 1
    event_path = Path(event_path_value)
    repository = os.environ.get("GITHUB_REPOSITORY", "")
    if not event_path.is_file():
        print(
            "GITHUB_EVENT_PATH должен указывать на pull request event payload.",
            file=sys.stderr,
        )
        return 1
    if not repository or len(repository.split("/")) != 2:
        print(
            "GITHUB_REPOSITORY должен иметь формат <owner>/<repo>.",
            file=sys.stderr,
        )
        return 1

    try:
        event = json.loads(event_path.read_text(encoding="utf-8"))
        if not isinstance(event, dict):
            raise ValidationError("GitHub event payload должен быть JSON object.")
        pull_request = event.get("pull_request")
        if not isinstance(pull_request, dict):
            raise ValidationError("В GitHub event отсутствует payload pull_request.")
        pull_request_number = pull_request.get("number")
        head = pull_request.get("head")
        if (
            not isinstance(pull_request_number, int)
            or pull_request_number < 1
            or not isinstance(head, dict)
            or not isinstance(head.get("sha"), str)
        ):
            raise ValidationError("В pull_request event отсутствуют number/head SHA.")

        result = validate_current_pull_request(
            repository,
            pull_request_number,
            expected_head_sha=head["sha"],
        )
    except (OSError, json.JSONDecodeError, ValidationError) as exception:
        print(f"PR workflow validation не пройдена: {exception}", file=sys.stderr)
        return 1

    print(f"PR workflow validation пройдена: {result.message}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
