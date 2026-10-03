#!/usr/bin/env python3
import argparse

from repair_engine import DiagnosticParser, FixStrategy, RepairEngine


class GitManager:
    def __init__(self, root_dir: str):
        self.root_dir = root_dir

    def is_dirty(self) -> bool:
        import subprocess

        result = subprocess.run(
            ["git", "status", "--porcelain"],
            cwd=self.root_dir,
            capture_output=True,
            text=True,
            check=False,
        )
        return bool(result.stdout.strip())

    def create_backup_branch(self) -> str:
        import subprocess
        import time

        branch_name = f"backup/repair-{time.strftime('%Y%m%d-%H%M%S')}"
        subprocess.run(
            ["git", "checkout", "-b", branch_name],
            cwd=self.root_dir,
            check=True,
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        return branch_name


def main():
    parser = argparse.ArgumentParser(description="Compatibility wrapper for SS14 Auto Repair")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--max-iter", type=int, default=5)
    args = parser.parse_args()

    engine = RepairEngine(is_dry_run=args.dry_run, max_iterations=args.max_iter)
    engine.run_ci()


if __name__ == "__main__":
    main()


__all__ = ["RepairEngine", "DiagnosticParser", "FixStrategy", "GitManager"]
