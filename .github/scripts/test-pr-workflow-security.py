#!/usr/bin/env python3
"""Проверяет zero-permission границу PR-head validation workflow."""

from __future__ import annotations

from pathlib import Path
import os
import re
import shutil
import subprocess
import tempfile
import textwrap
import unittest


WORKFLOW_PATH = (
    Path(__file__).resolve().parent.parent / "workflows" / "build.yml"
)
PR_VALIDATION_JOB = re.compile(
    r"(?ms)^  pr-workflow-validation:\n(?P<job>.*?)(?=^  [A-Za-z0-9_-]+:\n|\Z)"
)


class PullRequestWorkflowSecurityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls) -> None:
        cls.workflow = WORKFLOW_PATH.read_text(encoding="utf-8")
        match = PR_VALIDATION_JOB.search(cls.workflow)
        if match is None:
            raise AssertionError("В build.yml отсутствует pr-workflow-validation job.")
        cls.job = match.group("job")

    def _fetch_step_script(self) -> str:
        step_match = re.search(
            r"(?ms)^      - name: Fetch the public pull request head without credentials\n"
            r"(?P<step>.*?)(?=^      - name:|\Z)",
            self.job,
        )
        self.assertIsNotNone(step_match)
        run_match = re.search(
            r"(?ms)^        run: \|\n(?P<script>.*)\Z",
            step_match.group("step"),
        )
        self.assertIsNotNone(run_match)
        return textwrap.dedent(run_match.group("script"))

    @staticmethod
    def _credential_patterns() -> tuple[str, ...]:
        return (
            r"actions/checkout",
            r"\bgithub\s*(?:\.\s*token|\[\s*['\"]token['\"]\s*\])",
            r"\b(?:GITHUB_TOKEN|GH_TOKEN)\b",
            r"\bAuthorization\s*:",
            r"\bsecrets\s*(?:\.|\[\s*['\"][^'\"]+['\"]\s*\])",
        )

    @staticmethod
    def _candidate_execution_pattern() -> re.Pattern[str]:
        return re.compile(
            r"(?im)(?:^|[;&|]\s*|\bthen\s+|\bdo\s+|-\s*run\s*:\s*|\brun\s*:\s*)\s*"
            r"(?:(?:python(?:[0-9]+(?:\.[0-9]+)*)?|bash|sh)\s+"
            r"(?:-[^\s]+\s+)*|(?:source|\.)\s+)?(?:\.?/)?\.github/scripts/\S+"
            r"|(?:^|[;&|]\s*|\bthen\s+|\bdo\s+|-\s*run\s*:\s*|\brun\s*:\s*)\s*"
            r"(?:python(?:[0-9]+(?:\.[0-9]+)*)?|bash|sh)\s+"
            r"(?:-[^\s]+\s+)*[^\s]+\.(?:py|sh|bash)"
            r"(?:\s|[;&|]|$)"
        )

    def test_pr_head_job_has_no_token_permissions_or_checkout_action(self) -> None:
        self.assertRegex(self.job, r"(?m)^\s+permissions: \{\}\s*$")
        for pattern in self._credential_patterns():
            with self.subTest(forbidden_pattern=pattern):
                self.assertNotRegex(self.job, re.compile(pattern, re.IGNORECASE))

    def test_token_scan_covers_dot_and_bracket_expressions(self) -> None:
        token_pattern = re.compile(self._credential_patterns()[1], re.IGNORECASE)
        environment_token_pattern = re.compile(
            self._credential_patterns()[2],
            re.IGNORECASE,
        )
        authorization_pattern = re.compile(
            self._credential_patterns()[3],
            re.IGNORECASE,
        )
        secret_pattern = re.compile(self._credential_patterns()[4], re.IGNORECASE)
        for expression in ("github.token", "github['token']", 'github["token"]'):
            with self.subTest(expression=expression):
                self.assertRegex(expression, token_pattern)
        for expression in ("GITHUB_TOKEN", "GH_TOKEN"):
            with self.subTest(expression=expression):
                self.assertRegex(expression, environment_token_pattern)
        self.assertRegex("Authorization: Bearer", authorization_pattern)
        for expression in (
            "secrets.X",
            "secrets['X']",
            'secrets["X"]',
        ):
            with self.subTest(expression=expression):
                self.assertRegex(expression, secret_pattern)

    def test_public_fetch_disables_credentials(self) -> None:
        self.assertIn("GIT_TERMINAL_PROMPT: 0", self.job)
        self.assertIn("GIT_CONFIG_NOSYSTEM: 1", self.job)
        self.assertIn("GIT_CONFIG_GLOBAL: /dev/null", self.job)
        self.assertIn(
            "git -c credential.helper= -c http.extraheader= fetch",
            self.job,
        )
        self.assertIn(
            '"https://github.com/${GITHUB_REPOSITORY}.git"',
            self.job,
        )
        self.assertIn('"refs/pull/${PR_NUMBER}/head"', self.job)

    def _sha_helper_source(self) -> str:
        match = re.search(
            r"(?ms)^          verify_expected_head\(\) \{\n.*?^          \}\n",
            self.job,
        )
        self.assertIsNotNone(match)
        return textwrap.dedent(match.group(0))

    def _run_sha_helper_fixture(
        self,
        fetched_sha: str,
        expected_sha: str,
    ) -> tuple[subprocess.CompletedProcess[str], bool, bool]:
        bash_candidates = [shutil.which("bash")]
        if os.name == "nt":
            for program_files in (
                os.environ.get("ProgramFiles"),
                os.environ.get("ProgramFiles(x86)"),
            ):
                if program_files:
                    bash_candidates.append(
                        str(Path(program_files) / "Git" / "bin" / "bash.exe")
                    )
        bash = None
        for candidate in dict.fromkeys(value for value in bash_candidates if value):
            try:
                probe = subprocess.run(
                    [candidate, "-c", "exit 0"],
                    capture_output=True,
                    text=True,
                    check=False,
                    timeout=5,
                )
            except (OSError, subprocess.TimeoutExpired):
                continue
            if probe.returncode == 0:
                bash = candidate
                break
        if bash is None:
            self.skipTest("Для executable SHA-gate regression test требуется bash.")

        helper = self._sha_helper_source()
        with tempfile.TemporaryDirectory() as temp_directory:
            sentinel = Path(temp_directory) / "candidate-validation-ran"
            checkout_sentinel = Path(temp_directory) / "candidate-checkout-ran"
            script = (
                f"{helper}\n"
                'git() { if [[ "$1" == "checkout" ]]; then '
                'printf "checkout ran" > candidate-checkout-ran; fi; }\n'
                'verify_expected_head "$FETCHED_SHA" "$EXPECTED_SHA"\n'
                'git checkout --detach "$EXPECTED_SHA"\n'
                'printf "candidate validation ran" > candidate-validation-ran\n'
            )
            environment = {
                **os.environ,
                "FETCHED_SHA": fetched_sha,
                "EXPECTED_SHA": expected_sha,
            }
            result = subprocess.run(
                [bash, "-c", script],
                env=environment,
                cwd=temp_directory,
                capture_output=True,
                text=True,
                check=False,
            )
            return result, checkout_sentinel.exists(), sentinel.exists()

    def test_fetched_head_must_match_event_sha_before_validation(self) -> None:
        sha_check = 'verify_expected_head "$fetched_sha" "$EXPECTED_HEAD_SHA"'
        checkout = 'git checkout --detach "$EXPECTED_HEAD_SHA"'
        self.assertIn(sha_check, self.job)
        self.assertIn(checkout, self.job)
        self.assertNotIn("continue-on-error:", self.job)
        script = self._fetch_step_script()
        self.assertLess(script.index(sha_check), script.index(checkout))
        self.assertLess(
            self.job.index(checkout),
            self.job.index("python3 .github/scripts/test-check-pr-approved-plan.py"),
        )

    def test_sha_mismatch_exits_without_set_e_before_checkout_or_validation(self) -> None:
        result, checkout_ran, candidate_validation_ran = self._run_sha_helper_fixture(
            "a" * 40,
            "b" * 40,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertFalse(checkout_ran)
        self.assertFalse(candidate_validation_ran)

    def test_sha_match_allows_candidate_validation(self) -> None:
        sha = "a" * 40
        result, checkout_ran, candidate_validation_ran = self._run_sha_helper_fixture(
            sha,
            sha,
        )

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(checkout_ran)
        self.assertTrue(candidate_validation_ran)

    def test_candidate_code_cannot_run_before_exact_sha_gate(self) -> None:
        sha_check = 'verify_expected_head "$fetched_sha" "$EXPECTED_HEAD_SHA"'
        self.assertIn(sha_check, self.job)
        pre_sha_region = self.job.split(sha_check, maxsplit=1)[0]
        self.assertNotRegex(pre_sha_region, re.compile(r"\bgit\s+checkout\b", re.IGNORECASE))
        self.assertNotRegex(
            pre_sha_region,
            re.compile(r"(?im)^\s*uses:\s*\./"),
        )
        self.assertNotRegex(
            pre_sha_region,
            self._candidate_execution_pattern(),
        )

    def test_candidate_script_scan_covers_common_interpreters_and_paths(self) -> None:
        pattern = self._candidate_execution_pattern()
        for candidate_command in (
            "python3 .github/scripts/check-pr-approved-plan.py",
            "python candidate.py",
            "bash candidate.sh",
            "sh candidate.bash",
            "if true; then python3 .github/scripts/candidate.py; fi",
            "- run: python3 .github/scripts/candidate.py",
        ):
            with self.subTest(candidate_command=candidate_command):
                self.assertRegex(candidate_command, pattern)

    def test_pull_request_target_is_not_used(self) -> None:
        self.assertNotIn("pull_request_target:", self.workflow)

    def test_trusted_plan_freshness_test_is_not_called(self) -> None:
        self.assertNotIn("test-trusted-plan-freshness.py", self.workflow)

    def test_trusted_plan_freshness_workflow_is_removed(self) -> None:
        freshness_workflow = WORKFLOW_PATH.with_name("trusted-plan-freshness.yml")
        self.assertFalse(freshness_workflow.exists())

    def test_pr_body_edits_trigger_a_new_build_workflow_run(self) -> None:
        pull_request_events = re.search(
            r"(?ms)^  pull_request:\n(?P<events>.*?)(?=^  [A-Za-z0-9_-]+:\s*$|\Z)",
            self.workflow,
        )
        self.assertIsNotNone(pull_request_events)
        self.assertRegex(
            pull_request_events.group("events"),
            r"(?m)^\s+types:.*\bedited\b",
        )

    def test_checkout_credentials_are_not_persisted(self) -> None:
        checkout_steps = re.findall(
            r"(?ms)^      - name:.*?\n        uses: actions/checkout@.*?(?=^      - name:|\Z)",
            self.workflow,
        )
        self.assertGreater(len(checkout_steps), 0)
        for step in checkout_steps:
            with self.subTest(step=step.splitlines()[0]):
                self.assertIn("persist-credentials: false", step)

    def test_untrusted_ci_workflow_only_grants_content_read_globally(self) -> None:
        match = re.search(
            r"(?ms)^permissions:\n(?P<permissions>.*?)(?=^jobs:)",
            self.workflow,
        )
        self.assertIsNotNone(match)
        permissions = match.group("permissions")
        self.assertRegex(permissions, r"(?m)^\s+contents: read\s*$")
        self.assertNotRegex(permissions, r"(?m)^\s+(?:issues|pull-requests):")


if __name__ == "__main__":
    unittest.main()
