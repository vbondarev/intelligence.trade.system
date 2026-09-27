#!/usr/bin/env python3
"""Проверяет ссылки Pull Request на Issue и Approved Implementation Plan."""

from __future__ import annotations

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
ISSUE_CLOSE_LINE = re.compile(r"^\s*Closes\s+#([1-9][0-9]*)\s*$", re.IGNORECASE)
ISSUE_CLOSE_START = re.compile(r"^\s*Closes\b", re.IGNORECASE)
PLAN_FIELD_LINE = re.compile(r"^\s*Approved Implementation Plan:\s*(.*?)\s*$")
BASE_AMENDMENT_LINK_LINE = re.compile(
    r"^\s*Base Approved Implementation Plan:\s*(.*?)\s*$"
)
COMMENT_ID_FRAGMENT = re.compile(r"^issuecomment-([1-9][0-9]*)$")


class ValidationError(Exception):
    """Ошибка проверки PR workflow metadata."""


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


def parse_plan_permalink(
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
        or not path_parts[3].isdigit()
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


def comment_marker(comment: dict[str, object]) -> str:
    body = comment.get("body")
    if not isinstance(body, str):
        return ""
    lines = body.splitlines()
    return lines[0] if lines else ""


def comment_id(comment: dict[str, object]) -> str:
    value = comment.get("id")
    return str(value) if isinstance(value, (int, str)) else ""


def amendment_base_permalink(comment: dict[str, object]) -> str:
    body = comment.get("body")
    if not isinstance(body, str):
        raise ValidationError("Не удалось прочитать body Approved Plan Amendment.")
    values = [
        match.group(1)
        for line in body.splitlines()
        if (match := BASE_AMENDMENT_LINK_LINE.fullmatch(line)) is not None
    ]
    if len(values) != 1 or not values[0]:
        raise ValidationError(
            "Каждый Approved Plan Amendment должен содержать ровно один "
            "permalink в поле 'Base Approved Implementation Plan:'."
        )
    return values[0]


def validate_pull_request(
    event: dict[str, object],
    repository: str,
    issue_comments: list[dict[str, object]],
) -> str:
    pull_request = event.get("pull_request")
    if not isinstance(pull_request, dict):
        raise ValidationError("В GitHub event отсутствует payload pull_request.")
    body = pull_request.get("body")
    if not isinstance(body, str):
        body = ""

    issue_number = linked_issue_number(body)
    base_permalink = plan_field_value(body)
    listed_amendment_values = amendment_values(body)

    issue_comment_map = {
        comment_id(comment): comment
        for comment in issue_comments
        if comment_id(comment)
    }
    base_comments = [
        comment
        for comment in issue_comments
        if comment_marker(comment) == BASE_PLAN_MARKER
    ]
    amendment_comments = [
        comment
        for comment in issue_comments
        if comment_marker(comment) == AMENDMENT_MARKER
    ]
    if len(base_comments) > 1:
        raise ValidationError(
            "В Issue найдено несколько canonical Approved Implementation Plans."
        )

    if base_permalink == "N/A":
        if base_comments:
            raise ValidationError(
                "Нельзя использовать 'N/A': в связанной Issue есть Approved Plan."
            )
        if amendment_comments:
            raise ValidationError(
                "В Issue есть Approved Plan Amendments без базового Plan."
            )
        if listed_amendment_values != ["Нет"]:
            raise ValidationError(
                "При 'N/A' в PR в Approved Plan Amendments укажите '- Нет'."
            )
        return f"Issue #{issue_number}: Approved Plan и amendments отсутствуют."

    base_comment_id = parse_plan_permalink(
        base_permalink,
        repository,
        issue_number,
        BASE_PLAN_FIELD,
    )
    if len(base_comments) != 1:
        raise ValidationError(
            "В связанной Issue должен быть ровно один canonical Approved Plan comment."
        )
    base_comment = base_comments[0]
    if comment_id(base_comment) != str(base_comment_id):
        raise ValidationError(
            "Permalink Approved Implementation Plan не указывает на canonical "
            "base Plan comment."
        )
    for amendment in amendment_comments:
        if amendment_base_permalink(amendment) != base_permalink:
            raise ValidationError(
                "Approved Plan Amendment ссылается не на связанный base Plan."
            )

    if listed_amendment_values == ["Нет"]:
        listed_amendment_ids: list[int] = []
    else:
        listed_amendment_ids = [
            parse_plan_permalink(
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
            "В PR перечислите каждый canonical Approved Plan Amendment ровно один "
            "раз и в порядке comments Issue."
        )

    for amendment_id in listed_amendment_ids:
        amendment = issue_comment_map.get(str(amendment_id))
        if amendment is None:
            raise ValidationError(
                "Указанный Approved Plan Amendment отсутствует в связанной Issue."
            )
        if comment_marker(amendment) != AMENDMENT_MARKER:
            raise ValidationError(
                "Указанный comment не является canonical Approved Plan Amendment."
            )
        if amendment_base_permalink(amendment) != base_permalink:
            raise ValidationError(
                "Указанный Approved Plan Amendment ссылается не на связанный base Plan."
            )

    return (
        f"Issue #{issue_number}, Approved Plan comment #{base_comment_id}, "
        f"{len(listed_amendment_ids)} amendment(s) validated."
    )


def request_json(url: str, token: str) -> object:
    request = Request(
        url,
        headers={
            "Accept": "application/vnd.github+json",
            "Authorization": f"Bearer {token}",
            "User-Agent": "intelligence-trade-system-agent-workflow-validator",
            "X-GitHub-Api-Version": "2022-11-28",
        },
    )
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


def fetch_issue_comments(
    repository: str,
    issue_number: int,
    token: str,
) -> list[dict[str, object]]:
    if not token:
        raise ValidationError("Для проверки Issue comments требуется GITHUB_TOKEN.")

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
    token = os.environ.get("GITHUB_TOKEN", "")
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
        body = pull_request.get("body")
        if not isinstance(body, str):
            body = ""
        issue_number = linked_issue_number(body)
        comments = fetch_issue_comments(repository, issue_number, token)
        result = validate_pull_request(event, repository, comments)
    except (OSError, json.JSONDecodeError, ValidationError) as exception:
        print(f"PR workflow validation не пройдена: {exception}", file=sys.stderr)
        return 1

    print(f"PR workflow validation пройдена: {result}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
