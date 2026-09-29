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

    def test_pr_head_job_has_no_token_permissions_or_checkout_action(self) -> None:
        self.assertRegex(self.job, r"(?m)^\s+permissions: \{\}\s*$")
        for forbidden in (
            "actions/checkout",
            "github.token",
            "GITHUB_TOKEN",
            "GH_TOKEN",
            "Authorization:",
            "secrets.",
        ):
            with self.subTest(forbidden=forbidden):
                self.assertNotIn(forbidden, self.job)

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
    ) -> tuple[subprocess.CompletedProcess[str], bool]:
        bash = shutil.which("bash")
        if bash is None:
            self.skipTest("Для executable SHA-gate regression test требуется bash.")

        helper = self._sha_helper_source()
        with tempfile.TemporaryDirectory() as temp_directory:
            sentinel = Path(temp_directory) / "candidate-validation-ran"
            script = (
                "set -euo pipefail\n"
                f"{helper}\n"
                'verify_expected_head "$FETCHED_SHA" "$EXPECTED_SHA"\n'
                'printf "candidate validation ran" > "$CANDIDATE_SENTINEL"\n'
            )
            environment = {
                **os.environ,
                "FETCHED_SHA": fetched_sha,
                "EXPECTED_SHA": expected_sha,
                "CANDIDATE_SENTINEL": str(sentinel),
            }
            result = subprocess.run(
                [bash, "-c", script],
                env=environment,
                capture_output=True,
                text=True,
                check=False,
            )
            return result, sentinel.exists()

    def test_fetched_head_must_match_event_sha_before_validation(self) -> None:
        sha_check = 'verify_expected_head "$fetched_sha" "$EXPECTED_HEAD_SHA"'
        checkout = 'git checkout --detach "$EXPECTED_HEAD_SHA"'
        self.assertIn(sha_check, self.job)
        self.assertIn(checkout, self.job)
        self.assertNotIn("continue-on-error:", self.job)
        self.assertLess(self.job.index(sha_check), self.job.index(checkout))
        self.assertLess(
            self.job.index(checkout),
            self.job.index("python3 .github/scripts/test-check-pr-approved-plan.py"),
        )

    def test_sha_mismatch_fails_before_candidate_validation(self) -> None:
        result, candidate_validation_ran = self._run_sha_helper_fixture(
            "a" * 40,
            "b" * 40,
        )

        self.assertNotEqual(0, result.returncode)
        self.assertFalse(candidate_validation_ran)

    def test_sha_match_allows_candidate_validation(self) -> None:
        sha = "a" * 40
        result, candidate_validation_ran = self._run_sha_helper_fixture(sha, sha)

        self.assertEqual(0, result.returncode, result.stderr)
        self.assertTrue(candidate_validation_ran)

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
