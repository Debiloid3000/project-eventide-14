#!/usr/bin/env python3
import argparse
import json
import logging
import os
import re
import subprocess
from datetime import datetime
from pathlib import Path

os.makedirs("logs", exist_ok=True)
os.makedirs("reports", exist_ok=True)

log_filename = f"logs/repair_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

logging.basicConfig(
    level=logging.INFO,
    format="%(asctime)s - %(levelname)s: %(message)s",
    handlers=[
        logging.FileHandler(log_filename, encoding="utf-8"),
        logging.StreamHandler(),
    ],
)
log = logging.getLogger("SS14RepairPro")

TARGET_SOLUTION = "SpaceStation14.sln"
CACHE_DIR = Path(".repair_cache")
REFS_DIR = CACHE_DIR / "repos"

REFERENCE_REPOS = {
    "upstream": "https://github.com/space-syndicate/space-station-14.git",
    "genesis": "https://github.com/BrigChill3000/genesis-station-14.git",
    "sunrise": "https://github.com/makura-games/sunrise-station.git",
    "deadspace": "https://github.com/dead-space-server/dead-space-14.git",
    "goob": "https://github.com/space-syndicate/Goob-Station.git",
}


class DiagnosticParser:
    CS_ERROR_REGEX = re.compile(
        r"(?P<file>.*\.cs)\((?P<line>\d+),(?P<col>\d+)\):\s+error\s+(?P<code>CS\d+):\s+(?P<msg>.*)"
    )

    @staticmethod
    def run_build(working_dir: Path):
        log.info(f"Запуск dotnet build в {working_dir}...")
        result = subprocess.run(
            ["dotnet", "build", TARGET_SOLUTION, "-c", "Release", "/m"],
            cwd=working_dir,
            capture_output=True,
            text=True,
            encoding="utf-8",
        )

        errors = []
        output = (result.stdout or "") + "\n" + (result.stderr or "")
        for line in output.splitlines():
            match = DiagnosticParser.CS_ERROR_REGEX.search(line)
            if match:
                err = match.groupdict()
                err["file"] = str(Path(err["file"]).resolve())
                errors.append(err)

        return errors, result.returncode == 0


class SearchEngine:
    def __init__(self):
        self.setup_refs()

    def setup_refs(self):
        REFS_DIR.mkdir(parents=True, exist_ok=True)

        for name, url in REFERENCE_REPOS.items():
            repo_path = REFS_DIR / name
            if repo_path.exists():
                continue

            log.info(f"Клонирование {name}...")
            subprocess.run(
                ["git", "clone", "--depth", "1", url, str(repo_path)],
                check=False,
            )

    def find_type_definition(self, type_name: str):
        regex = re.compile(rf"\b(?:class|struct|interface|enum|record)\s+{re.escape(type_name)}\b")
        ns_regex = re.compile(r"namespace\s+([\w\.]+)")

        results = {}

        for repo_name in REFERENCE_REPOS.keys():
            repo_path = REFS_DIR / repo_name
            if not repo_path.exists():
                continue

            for root, _, files in os.walk(repo_path):
                for file in files:
                    if not file.endswith(".cs"):
                        continue

                    filepath = Path(root) / file
                    try:
                        with open(filepath, "r", encoding="utf-8") as f:
                            content = f.read()
                    except Exception:
                        continue

                    if not regex.search(content):
                        continue

                    ns_match = ns_regex.search(content)
                    ns = ns_match.group(1) if ns_match else None
                    results[repo_name] = {
                        "file": str(filepath),
                        "namespace": ns,
                        "content": content,
                    }
                    break

        return results


class FixStrategy:
    def __init__(self, search_engine: SearchEngine, staging_dir: Path):
        self.search = search_engine
        self.staging_dir = staging_dir
        self.fixes_applied = []

    def attempt_fix(self, error) -> bool:
        code = error.get("code")
        msg = error.get("msg", "")
        file_path = error.get("file")

        if not file_path or not code:
            return False

        try:
            rel_path = os.path.relpath(file_path, start=os.getcwd())
            staging_file_path = self.staging_dir / rel_path
        except ValueError:
            return False

        if not staging_file_path.exists():
            return False

        if code == "CS0246":
            match = re.search(r"The type or namespace name '(\w+)' could not be found", msg)
            if match:
                missing_type = match.group(1)
                return self._handle_cs0246(staging_file_path, missing_type)

        return False

    def _handle_cs0246(self, file_path: Path, missing_type: str) -> bool:
        search_results = self.search.find_type_definition(missing_type)
        if not search_results:
            return False

        # Prefer upstream as the most canonical reference
        if "upstream" in search_results:
            ns = search_results["upstream"]["namespace"]
            if ns:
                return self._add_using(file_path, ns, missing_type)

        for repo_name in ("genesis", "sunrise", "deadspace", "goob"):
            if repo_name in search_results:
                ns = search_results[repo_name]["namespace"]
                if ns:
                    return self._add_using(file_path, ns, missing_type)

        return False

    def _add_using(self, file_path: Path, namespace: str, type_name: str) -> bool:
        try:
            with open(file_path, "r", encoding="utf-8") as f:
                lines = f.readlines()
        except Exception:
            return False

        using_str = f"using {namespace};\n"
        if any(using_str.strip() == line.strip() for line in lines):
            return False

        last_using_idx = -1
        for i, line in enumerate(lines):
            if line.startswith("using "):
                last_using_idx = i

        if last_using_idx != -1:
            lines.insert(last_using_idx + 1, using_str)
        else:
            lines.insert(0, using_str)

        with open(file_path, "w", encoding="utf-8") as f:
            f.writelines(lines)

        self.fixes_applied.append(
            {
                "type": "add_using",
                "file": str(file_path),
                "namespace": namespace,
                "type_name": type_name,
            }
        )
        return True


class RepairEngine:
    def __init__(self, is_dry_run: bool = False, max_iterations: int = 5):
        self.is_dry_run = is_dry_run
        self.max_iter = max_iterations
        self.search = SearchEngine()
        self.start_time = datetime.now()
        self.report = {
            "started_at": self.start_time.isoformat(),
            "initial_errors": 0,
            "final_errors": 0,
            "iterations": 0,
            "fixes_applied": [],
            "status": "pending",
            "failure_reason": None,
            "finished_at": None,
        }

    def run_ci(self):
        cwd = Path(os.getcwd())
        errors, success = DiagnosticParser.run_build(cwd)
        self.report["initial_errors"] = len(errors)

        if success or len(errors) == 0:
            log.info("Ошибок сборки не обнаружено.")
            self.report["status"] = "success"
            self.report["failure_reason"] = "NO_ERRORS_FOUND"
            self._set_github_output(has_fixes=False)
            self.save_report()
            return

        strategy = FixStrategy(self.search, cwd)
        prev_errors_count = len(errors)

        for iteration in range(1, self.max_iter + 1):
            log.info(f"--- Итерация {iteration} ---")
            fixes_this_round = 0

            for err in errors:
                if strategy.attempt_fix(err):
                    fixes_this_round += 1

            if fixes_this_round == 0:
                self.report["failure_reason"] = "NO_PROGRESS"
                self.report["status"] = "failed"
                break

            new_errors, new_success = DiagnosticParser.run_build(cwd)

            if len(new_errors) >= prev_errors_count:
                log.warning("После исправлений количество ошибок не уменьшилось. Откатываем изменения.")
                subprocess.run(["git", "checkout", "."], cwd=cwd, check=False)
                subprocess.run(["git", "clean", "-fd"], cwd=cwd, check=False)
                self.report["failure_reason"] = "MADE_WORSE"
                self.report["status"] = "failed"
                break

            errors = new_errors
            prev_errors_count = len(errors)
            self.report["iterations"] = iteration

            if new_success:
                self.report["status"] = "success"
                break

        self.report["final_errors"] = len(errors)
        self.report["fixes_applied"] = strategy.fixes_applied
        self.report["finished_at"] = datetime.now().isoformat()

        # Установить has_fixes только если были применены исправления И финальная сборка успешна
        has_fixes = len(strategy.fixes_applied) > 0 and self.report["status"] == "success"
        self._set_github_output(has_fixes=has_fixes)
        self.save_report()

    def _set_github_output(self, has_fixes: bool):
        github_output = os.getenv("GITHUB_OUTPUT")
        if github_output:
            with open(github_output, "a", encoding="utf-8") as f:
                f.write(f"has_fixes={'true' if has_fixes else 'false'}\n")

    def save_report(self):
        report_path = f"reports/repair_summary_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
        with open(report_path, "w", encoding="utf-8") as f:
            json.dump(self.report, f, indent=4, ensure_ascii=False)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SS14 Autonomous PRO Repair Engine")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--max-iter", type=int, default=5)
    args = parser.parse_args()

    engine = RepairEngine(is_dry_run=args.dry_run, max_iterations=args.max_iter)
    engine.run_ci()
