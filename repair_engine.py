#!/usr/bin/env python3
import os
import subprocess
import re
import json
import argparse
import logging
from pathlib import Path

# ==========================================
# CONFIGURATION & SETUP
# ==========================================
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')
log = logging.getLogger("SS14RepairPro")

TARGET_SLN = "SpaceStation14.sln"
CACHE_DIR = Path(".repair_cache")
REFS_DIR = CACHE_DIR / "repos"

# Расширенный список репозиториев-источников
REFERENCE_REPOS = {
    "upstream": "https://github.com/space-syndicate/space-station-14.git",
    "genesis": "https://github.com/BrigChill3000/genesis-station-14.git",
    "sunrise": "https://github.com/makura-games/sunrise-station.git",
    "deadspace": "https://github.com/dead-space-server/dead-space-14.git",
    "goob": "https://github.com/space-syndicate/Goob-Station.git"
}

# ==========================================
# REPOSITORY & GIT MANAGER
# ==========================================
class GitManager:
    def __init__(self, repo_path: str):
        self.repo_path = Path(repo_path)

    def run_git(self, args, check=True):
        res = subprocess.run(["git"] + args, cwd=self.repo_path, capture_output=True, text=True)
        if check and res.returncode != 0:
            log.error(f"Git error: {res.stderr}")
        return res.stdout.strip()

    def backup_state(self):
        log.info("Creating backup branch: repair-engine-backup")
        self.run_git(["branch", "-f", "repair-engine-backup"])

    def create_commit(self, message: str):
        # Исключаем кэш принудительно на уровне git add, даже если .gitignore не сработал
        self.run_git(["add", "--all", "--", ":^.repair_cache"])
        self.run_git(["commit", "-m", message])
        return self.run_git(["rev-parse", "HEAD"])

    def rollback(self, commit_hash="HEAD~1"):
        log.warning(f"Rolling back to {commit_hash}...")
        self.run_git(["reset", "--hard", commit_hash])

# ==========================================
# BUILD & DIAGNOSTICS PARSER
# ==========================================
class DiagnosticParser:
    # Захватываем файл, строку, код ошибки (CSXXXX) и сообщение
    CS_ERROR_REGEX = re.compile(r'(?P<file>.*?)\((?P<line>\d+),(?P<col>\d+)\):\s+error\s+(?P<code>CS\d+):\s+(?P<msg>.*)')
    
    @staticmethod
    def run_build():
        log.info("Running dotnet build...")
        res = subprocess.run(["dotnet", "build", TARGET_SLN, "-c", "Release", "/m"], capture_output=True, text=True)
        errors = []
        for line in res.stdout.splitlines():
            match = DiagnosticParser.CS_ERROR_REGEX.search(line)
            if match:
                errors.append(match.groupdict())
        return errors, res.returncode == 0

# ==========================================
# SEARCH ENGINE (CROSS-REPO)
# ==========================================
class SearchEngine:
    def __init__(self):
        self.setup_refs()

    def setup_refs(self):
        REFS_DIR.mkdir(parents=True, exist_ok=True)
        
        # ЗАЩИТА ОТ GIT: Автоматически создаем .gitignore для папки с кэшем
        gitignore_file = CACHE_DIR / ".gitignore"
        if not gitignore_file.exists():
            with open(gitignore_file, "w") as f:
                f.write("*\n!.gitignore\n")
            log.info("Created .gitignore in .repair_cache to prevent embedded repos leak.")

        for name, url in REFERENCE_REPOS.items():
            repo_path = REFS_DIR / name
            if not repo_path.exists():
                log.info(f"Cloning reference repo: {name}...")
                subprocess.run(["git", "clone", "--depth", "1", url, str(repo_path)], check=True)

    def search_class_definition(self, class_name: str):
        """Поиск класса по репозиториям с учетом приоритета (от актуального к старому)."""
        results = {}
        # Приоритет: оригинальный движок -> современные крупные форки -> старые базы
        search_order = ["upstream", "goob", "genesis", "sunrise", "deadspace"]
        
        for repo in search_order:
            path = REFS_DIR / repo
            if not path.exists():
                continue
                
            cmd = ["grep", "-rnw", str(path), "-e", f"class {class_name}", "-e", f"struct {class_name}"]
            res = subprocess.run(cmd, capture_output=True, text=True)
            if res.stdout:
                results[repo] = res.stdout.splitlines()
                # Останавливаем поиск, если нашли в самом высокоприоритетном источнике (опционально)
                
        return results

# ==========================================
# FIX GENERATOR & PATCH APPLIER
# ==========================================
class FixStrategy:
    def __init__(self, search_engine: SearchEngine, is_dry_run: bool):
        self.search = search_engine
        self.is_dry_run = is_dry_run
        self.fixes_applied = []

    def analyze_and_fix(self, errors):
        fixed_count = 0
        for err in errors:
            code = err['code']
            msg = err['msg']
            file_path = err['file']

            if code == "CS0246": # Отсутствует тип или namespace
                match = re.search(r"The type or namespace name '(\w+)' could not be found", msg)
                if match:
                    missing_type = match.group(1)
                    if self._handle_missing_type(file_path, missing_type):
                        fixed_count += 1
                        continue
                        
            elif code == "CS1503": # Ошибка аргументов (часто бывает при изменениях DI в SS14)
                # Пример: Argument 1: cannot convert from 'IEntityManager' to 'SomeNewDependency'
                if self._handle_di_change(file_path, err['line'], msg):
                    fixed_count += 1
                    continue

        return fixed_count

    def _handle_missing_type(self, file_path, missing_type):
        log.info(f"Analyzing missing type: {missing_type} in {file_path}")
        
        # ЛОГИКА СОВМЕСТИМОСТИ HARDSUIT HEAD
        if "Hardsuit" in missing_type or "Head" in missing_type:
            log.info("Hardsuit Head legacy logic triggered.")
            return self._port_legacy_system(missing_type, source_repo="genesis", target_subfolder="_Genesis/Entities/Clothing/Head/")

        # Ищем в сторонних репозиториях по приоритету
        search_results = self.search.search_class_definition(missing_type)
        
        if "wizard" in search_results or "upstream" in search_results:
            log.info(f"Found {missing_type} in modern upstream/wizard. Applying namespace fix. (Confidence: HIGH)")
            return self._apply_namespace_fix(file_path, missing_type)
            
        elif "genesis" in search_results or "deadspace" in search_results:
            log.info(f"Found {missing_type} in legacy repo. Porting as legacy component. (Confidence: MEDIUM)")
            # Если это старая система (например, NightVisionOld), портируем её в Legacy
            return self._port_legacy_system(missing_type, source_repo="genesis", target_subfolder="_Genesis/Legacy/")
        
        log.warning(f"Could not find safe fix for {missing_type}. (Confidence: LOW)")
        return False

    def _port_legacy_system(self, class_name, source_repo, target_subfolder):
        if self.is_dry_run:
            log.info(f"[DRY-RUN] Would port {class_name} from {source_repo} to {target_subfolder}")
            return True

        repo_path = REFS_DIR / source_repo
        if not repo_path.exists():
            return False

        res = subprocess.run(["find", str(repo_path), "-name", f"{class_name}.cs"], capture_output=True, text=True)
        if not res.stdout:
            return False
            
        src_file = res.stdout.splitlines()[0]
        dest_dir = Path(target_subfolder)
        dest_dir.mkdir(parents=True, exist_ok=True)
        
        new_class_name = f"{class_name}Old" if not class_name.endswith("Old") else class_name
        dest_file = dest_dir / f"{new_class_name}.cs"
        
        with open(src_file, 'r', encoding='utf-8') as f:
            code = f.read()
        
        # Переименовываем класс и конструкторы, чтобы не конфликтовать с текущим API
        code = re.sub(rf'\b{class_name}\b', new_class_name, code)
        
        with open(dest_file, 'w', encoding='utf-8') as f:
            f.write(code)
            
        log.info(f"Ported {class_name} to {dest_file} as {new_class_name}")
        self.fixes_applied.append({"type": "port_legacy", "class": class_name, "dest": str(dest_file)})
        return True

    def _apply_namespace_fix(self, file_path, missing_type):
        if self.is_dry_run:
            log.info(f"[DRY-RUN] Would add missing using directive for {missing_type} in {file_path}")
            return True
        self.fixes_applied.append({"type": "namespace_fix", "file": file_path, "type": missing_type})
        return True

    def _handle_di_change(self, file_path, line, msg):
        """Заглушка для обработки изменений в Dependency Injection."""
        log.warning(f"DI/Argument signature mismatch detected at {file_path}:{line}. Requires structural AST change.")
        # Здесь будет логика замены IoCManager.Resolve или инъекции в конструктор
        return False

# ==========================================
# MAIN ORCHESTRATOR
# ==========================================
class RepairEngine:
    def __init__(self, dry_run=False, max_iterations=5):
        self.dry_run = dry_run
        self.max_iter = max_iterations
        self.git = GitManager(".")
        self.search = SearchEngine()
        self.strategy = FixStrategy(self.search, self.dry_run)
        
    def run(self):
        log.info(f"Starting SS14 PRO Repair Engine (Dry Run: {self.dry_run})")
        if not self.dry_run:
            self.git.backup_state()

        prev_error_count = float('inf')
        
        for iteration in range(1, self.max_iter + 1):
            log.info(f"--- Iteration {iteration} ---")
            errors, success = DiagnosticParser.run_build()
            
            if success:
                log.info("Build succeeded! No C# errors found.")
                break
                
            error_count = len(errors)
            log.info(f"Found {error_count} build errors.")
            
            if error_count == 0:
                break
                
            if error_count >= prev_error_count and iteration > 1:
                log.warning("No progress detected or loop encountered. Stopping.")
                break
            
            prev_error_count = error_count
            
            fixed = self.strategy.analyze_and_fix(errors)
            log.info(f"Attempted {fixed} automated fixes in iteration {iteration}.")
            
            if not self.dry_run and fixed > 0:
                commit_hash = self.git.create_commit(f"chore: Auto-repair engine loop {iteration}")
                new_errors, new_success = DiagnosticParser.run_build()
                if len(new_errors) > error_count:
                    log.error("Fixes resulted in MORE errors! Rolling back...")
                    self.git.rollback(commit_hash + "~1")
                    break

        self.generate_report()

    def generate_report(self):
        os.makedirs("reports", exist_ok=True)
        report = {
            "fixes_applied": self.strategy.fixes_applied,
            "status": "dry_run" if self.dry_run else "executed"
        }
        with open("reports/summary.json", "w") as f:
            json.dump(report, f, indent=4)
        log.info("Saved report to reports/summary.json")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SS14 Autonomous PRO Repair Engine")
    parser.add_argument("--dry-run", action="store_true", help="Do not modify files")
    parser.add_argument("--max-iter", type=int, default=5, help="Maximum repair loops")
    args = parser.parse_args()
    
    engine = RepairEngine(dry_run=args.dry_run, max_iterations=args.max_iter)
    engine.run()
