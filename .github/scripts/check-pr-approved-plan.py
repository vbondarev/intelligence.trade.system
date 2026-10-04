#!/usr/bin/env python3
"""Проверяет текущую связь PR → Issue → Approved Plan и Amendments."""

from __future__ import annotations

from dataclasses import dataclass
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
BASE_PLAN_FIELD = "Approved Implementation Plan:"
AMENDMENTS_FIELD = "Approved Plan Amendments:"
BASE_AMENDMENT_LINK_FIELD = "Base Approved Implementation Plan:"
TRUSTED_PLAN_AUTHOR = "vbondarev"
ISSUE_CLOSE_LINE = re.compile(r"^Closes\s+#([1-9][0-9]*)\s*$", re.IGNORECASE)
ISSUE_CLOSE_START = re.compile(r"^Closes\b", re.IGNORECASE)
FENCE_START = re.compile(r"^\s*(`{3,}|~{3,})")
PLAN_FIELD_LINE = re.compile(r"^Approved Implementation Plan:\s*(.*?)\s*$")
BASE_AMENDMENT_LINK_LINE = re.compile(
    r"^Base Approved Implementation Plan:\s*(.*?)\s*$"
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


def visible_markdown_lines(body: str) -> list[str]:
    visible_lines: list[str] = []
    in_html_comment = False
    fence_character = ""
    fence_length = 0

    for raw_line in body.splitlines():
        parts: list[str] = []
        cursor = 0
        while cursor < len(raw_line):
            if in_html_comment:
                comment_end = raw_line.find("-->", cursor)
                if comment_end < 0:
                    cursor = len(raw_line)
                    break
                in_html_comment = False
                cursor = comment_end + 3
                continue

            comment_start = raw_line.find("<!--", cursor)
            if comment_start < 0:
                parts.append(raw_line[cursor:])
                break
            parts.append(raw_line[cursor:comment_start])
            in_html_comment = True
            cursor = comment_start + 4

        line = "".join(parts)
        stripped = line.lstrip()

        if fence_character:
            if re.fullmatch(
                rf"{re.escape(fence_character)}{{{fence_length},}}\s*",
                stripped,
            ):
                fence_character = ""
                fence_length = 0
            continue

        fence_match = FENCE_START.match(line)
        if fence_match is not None:
            marker = fence_match.group(1)
            fence_character = marker[0]
            fence_length = len(marker)
            continue

        visible_lines.append(line)

    return visible_lines


def linked_issue_number(body: str) -> int:
    lines = body.splitlines()
    first_content_line = next((line for line in lines if line.strip()), "")
    match = ISSUE_CLOSE_LINE.fullmatch(first_content_line)
    if match is None:
        raise ValidationError(
            "Первая непустая строка PR body должна иметь точный вид "
            "'Closes #<issue>'."
        )

    close_lines = [
        line for line in visible_markdown_lines(body) if ISSUE_CLOSE_START.match(line)
    ]
    if len(close_lines) != 1:
        raise ValidationError(
            "В PR body должна быть ровно одна primary-ссылка вида 'Closes #<issue>'."
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
        port = parsed.port
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
        or port is not None
    ):
        raise ValidationError(f"{field_name}: укажите permalink GitHub Issue comment.")

    path_parts = parsed.path.strip("/").split("/")
    if (
        len(path_parts) != 4
        or path_parts[2].casefold() != "issues"
        or re.fullmatch(r"[1-9][0-9]*", path_parts[3]) is None
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
        for line in visible_markdown_lines(body)
        if (match := PLAN_FIELD_LINE.fullmatch(line)) is not None
    ]
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            "В PR body должно быть ровно одно непустое поле "
            "'Approved Implementation Plan:'."
        )
    return values[0]


def amendment_values(body: str) -> list[str]:
    lines = visible_markdown_lines(body)
    section_indices = [
        index for index, line in enumerate(lines) if line == AMENDMENTS_FIELD
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


def comment_url(
    comment: dict[str, object],
    repository: str,
    issue_number: int,
    role: str,
) -> str:
    url = comment.get("html_url")
    if not isinstance(url, str) or not url:
        raise ValidationError(f"У {role} отсутствует html_url.")
    linked_id = parse_issue_comment_permalink(
        url,
        repository,
        issue_number,
        role,
    )
    if comment_id(comment) != str(linked_id):
        raise ValidationError(f"Permalink {role} не совпадает с comment id.")
    return url


def amendment_base_permalink(comment: dict[str, object]) -> str:
    lines = visible_markdown_lines(comment_body(comment))
    fields: list[tuple[int, str]] = []
    for index, line in enumerate(lines):
        match = BASE_AMENDMENT_LINK_LINE.fullmatch(line)
        if match is not None:
            fields.append((index, match.group(1)))

    value = ""
    if len(fields) == 1:
        index, value = fields[0]
        if not value and index + 1 < len(lines):
            next_line = lines[index + 1].strip()
            if next_line:
                value = next_line

    if (
        len(fields) != 1
        or not value
        or value in {"<permalink>", "<base-permalink>"}
    ):
        raise ValidationError(
            "Каждый Approved Plan Amendment должен содержать ровно один "
            "permalink в поле 'Base Approved Implementation Plan:'."
        )
    return value


def validate_effective_artifacts(
    issue_comments: list[dict[str, object]],
    repository: str,
    issue_number: int,
) -> tuple[list[dict[str, object]], list[dict[str, object]]]:
    base_comments: list[dict[str, object]] = []
    amendments: list[dict[str, object]] = []

    for comment in issue_comments:
        marker = comment_marker(comment)
        if marker not in {BASE_PLAN_MARKER, AMENDMENT_MARKER}:
            continue
        if not is_trusted_plan_author(comment):
            continue

        role = "Canonical base Plan" if marker == BASE_PLAN_MARKER else "Amendment"
        identifier = comment_id(comment)
        if re.fullmatch(r"[1-9][0-9]*", identifier) is None:
            raise ValidationError(f"У {role} отсутствует корректный id.")
        comment_is_immutable(comment, role)
        comment_url(comment, repository, issue_number, role)

        if marker == BASE_PLAN_MARKER:
            base_comments.append(comment)
        else:
            amendments.append(comment)

    if len(base_comments) > 1:
        raise ValidationError(
            "В связанной Issue найдено несколько trusted canonical base Plans."
        )
    if amendments and not base_comments:
        raise ValidationError(
            "Trusted Approved Plan Amendment найден без trusted base Plan."
        )

    if base_comments:
        base_comment = base_comments[0]
        base_id = comment_id(base_comment)
        for amendment in amendments:
            amendment_base_id = parse_issue_comment_permalink(
                amendment_base_permalink(amendment),
                repository,
                issue_number,
                BASE_AMENDMENT_LINK_FIELD,
            )
            if amendment_base_id != int(base_id):
                raise ValidationError(
                    "Approved Plan Amendment ссылается не на active base Plan."
                )

    return base_comments, amendments


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

    if linked_issue_number(body) != issue_number:
        raise ValidationError("Closes и загруженная Issue имеют разные номера.")

    base_permalink = plan_field_value(body)
    listed_amendments = amendment_values(body)
    base_comments, amendments = validate_effective_artifacts(
        issue_comments,
        repository,
        issue_number,
    )

    if base_permalink == "N/A":
        if base_comments or amendments:
            raise ValidationError(
                "Нельзя использовать 'N/A': в Issue есть trusted Plan или Amendment."
            )
        if listed_amendments != ["Нет"]:
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
            "В связанной Issue должен быть ровно один trusted canonical Approved Plan."
        )
    base_comment = base_comments[0]
    if comment_id(base_comment) != str(base_comment_id):
        raise ValidationError(
            "Permalink Approved Implementation Plan не указывает на trusted "
            "canonical base Plan."
        )

    if listed_amendments == ["Нет"]:
        listed_amendment_ids: list[int] = []
    else:
        listed_amendment_ids = [
            parse_issue_comment_permalink(
                permalink,
                repository,
                issue_number,
                AMENDMENTS_FIELD,
            )
            for permalink in listed_amendments
        ]
        if len(set(listed_amendment_ids)) != len(listed_amendment_ids):
            raise ValidationError("В PR повторяется ссылка на Approved Plan Amendment.")

    expected_amendment_ids = [int(comment_id(comment)) for comment in amendments]
    if listed_amendment_ids != expected_amendment_ids:
        raise ValidationError(
            "В PR перечислите каждый current trusted Approved Plan Amendment "
            "ровно один раз и в порядке comments Issue."
        )

    return (
        f"Issue #{issue_number}, Approved Plan comment #{base_comment_id}, "
        f"{len(listed_amendment_ids)} amendment(s) validated."
    )


def request_json(url: str) -> object:
    request = Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "User-Agent": "intelligence-trade-system-agent-workflow-validator",
            "X-GitHub-Api-Version": API_VERSION,
        },
    )
    try:
        with urlopen(request, timeout=30) as response:
            return json.loads(response.read().decode("utf-8"))
    except HTTPError as exception:
        raise ValidationError(f"GitHub API вернул HTTP {exception.code}.") from None
    except URLError as exception:
        raise ValidationError(
            f"Не удалось выполнить запрос к GitHub API: {exception.reason}."
        ) from None
    except (json.JSONDecodeError, UnicodeDecodeError) as exception:
        raise ValidationError(f"GitHub API вернул некорректный JSON: {exception}.") from None
    except OSError as exception:
        raise ValidationError(f"Ошибка запроса к GitHub API: {exception}.") from None


def get_api_object(url: str) -> dict[str, object]:
    response = request_json(url)
    if not isinstance(response, dict):
        raise ValidationError("GitHub API вернул объект некорректного типа.")
    return response


def fetch_current_pull_request(
    repository: str,
    pull_request_number: int,
) -> dict[str, object]:
    encoded_repository = quote(repository, safe="/")
    pull_request = get_api_object(
        f"https://api.github.com/repos/{encoded_repository}/pulls/"
        f"{pull_request_number}"
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


def fetch_issue(repository: str, issue_number: int) -> dict[str, object]:
    encoded_repository = quote(repository, safe="/")
    return get_api_object(
        f"https://api.github.com/repos/{encoded_repository}/issues/{issue_number}"
    )


def fetch_issue_comments(
    repository: str,
    issue_number: int,
) -> list[dict[str, object]]:
    comments: list[dict[str, object]] = []
    page = 1
    encoded_repository = quote(repository, safe="/")
    while True:
        url = (
            f"https://api.github.com/repos/{encoded_repository}/issues/"
            f"{issue_number}/comments?per_page=100&page={page}"
        )
        response = request_json(url)
        if not isinstance(response, list) or any(
            not isinstance(comment, dict) for comment in response
        ):
            raise ValidationError("GitHub API вернул некорректный список Issue comments.")
        comments.extend(response)
        if len(response) < 100:
            return comments
        page += 1


def validate_current_pull_request(
    repository: str,
    pull_request_number: int,
    expected_head_sha: str | None = None,
) -> PullRequestValidation:
    pull_request = fetch_current_pull_request(repository, pull_request_number)
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
    issue = fetch_issue(repository, issue_number)
    if issue_is_pull_request(issue):
        raise ValidationError("Closes ссылается на Pull Request, а не на Issue.")
    issue_comments = fetch_issue_comments(repository, issue_number)
    message = validate_pull_request(body, repository, issue, issue_comments)
    return PullRequestValidation(
        message=message,
        issue_number=issue_number,
        pull_request_number=pull_request_number,
        head_sha=current_head_sha,
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
