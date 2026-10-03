#!/usr/bin/env python3
import os
import subprocess
import re
import json
import argparse
import logging
import shutil
from pathlib import Path
from datetime import datetime
import difflib

# Настройка логирования
os.makedirs("logs", exist_ok=True)
os.makedirs("reports", exist_ok=True)
log_filename = f"logs/repair_{datetime.now().strftime('%Y%m%d_%H%M%S')}.log"

logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(levelname)s: %(message)s',
    handlers=[logging.FileHandler(log_filename, encoding='utf-8'), logging.StreamHandler()]
)
log = logging.getLogger("SS14RepairPro")

TARGET_SLN = "SpaceStation14.sln"
CACHE_DIR = Path(".repair_cache")
REFS_DIR = CACHE_DIR / "repos"

REFERENCE_REPOS = {
    "upstream": "https://github.com/space-syndicate/space-station-14.git",
    "genesis": "https://github.com/BrigChill3000/genesis-station-14.git",
    "sunrise": "https://github.com/makura-games/sunrise-station.git",
    "deadspace": "https://github.com/dead-space-server/dead-space-14.git",
    "goob": "https://github.com/space-syndicate/Goob-Station.git"
}

class GitManager:
    def __init__(self, repo_path: str):
        self.repo_path = Path(repo_path)

    def run_git(self, args, check=False):
        res = subprocess.run(["git"] + args, cwd=self.repo_path, capture_output=True, text=True, encoding='utf-8')
        if check and res.returncode != 0:
            log.error(f"Git error: {res.stderr}")
        return res

    def is_dirty(self):
        res = self.run_git(["status", "--porcelain"])
        return bool(res.stdout.strip())

    def create_backup_branch(self):
        branch_name = f"repair-backup-{datetime.now().strftime('%Y%m%d%H%M%S')}"
        log.info(f"Создание резервной ветки: {branch_name}")
        self.run_git(["branch", branch_name], check=True)
        return branch_name

class DiagnosticParser:
    # Захватываем ошибки как из MSBuild, так и из Roslyn
    CS_ERROR_REGEX = re.compile(r'(?P<file>.*\.cs)\((?P<line>\d+),(?P<col>\d+)\):\s+error\s+(?P<code>CS\d+):\s+(?P<msg>.*)')

    @staticmethod
    def run_build(working_dir: Path):
        log.info(f"Запуск dotnet build в {working_dir}...")
        # Учитываем Windows и Linux. Объединяем stdout и stderr
        res = subprocess.run(
            ["dotnet", "build", TARGET_SLN, "-c", "Release", "/m"],
            cwd=working_dir, capture_output=True, text=True, encoding='utf-8'
        )
        errors = []
        output = res.stdout + "\n" + res.stderr
        for line in output.splitlines():
            match = DiagnosticParser.CS_ERROR_REGEX.search(line)
            if match:
                err = match.groupdict()
                # Нормализация путей
                err['file'] = str(Path(err['file']).resolve())
                errors.append(err)
        return errors, res.returncode == 0

class SearchEngine:
    def __init__(self):
        self.setup_refs()

    def setup_refs(self):
        REFS_DIR.mkdir(parents=True, exist_ok=True)
        gitignore_file = CACHE_DIR / ".gitignore"
        if not gitignore_file.exists():
            with open(gitignore_file, "w") as f:
                f.write("*\n!.gitignore\n")

        for name, url in REFERENCE_REPOS.items():
            repo_path = REFS_DIR / name
            if not repo_path.exists():
                log.info(f"Клонирование {name}...")
                subprocess.run(["git", "clone", "--depth", "1", url, str(repo_path)], check=False)
            else:
                # Асинхронное/быстрое обновление (fetch)
                subprocess.run(["git", "fetch", "--depth", "1"], cwd=repo_path, check=False)

    def find_type_definition(self, type_name: str):
        # Кроссплатформенный поиск без grep
        regex = re.compile(rf'\b(?:class|struct|interface|enum|record)\s+{type_name}\b')
        ns_regex = re.compile(r'namespace\s+([\w\.]+)')
        results = {}

        for repo in REFERENCE_REPOS.keys():
            repo_path = REFS_DIR / repo
            if not repo_path.exists(): continue

            for root, _, files in os.walk(repo_path):
                for file in files:
                    if file.endswith(".cs"):
                        filepath = Path(root) / file
                        try:
                            with open(filepath, 'r', encoding='utf-8') as f:
                                content = f.read()
                                if regex.search(content):
                                    # Пытаемся определить namespace
                                    ns_match = ns_regex.search(content)
                                    ns = ns_match.group(1) if ns_match else None
                                    results[repo] = {"file": filepath, "namespace": ns, "content": content}
                                    break # Нашли в этом репо
                        except Exception:
                            continue
        return results

class FixStrategy:
    def __init__(self, search_engine: SearchEngine, staging_dir: Path):
        self.search = search_engine
        self.staging_dir = staging_dir
        self.fixes_applied = []

    def attempt_fix(self, error) -> bool:
        code, msg, file_path = error['code'], error['msg'], error['file']
        
        # Конвертация абсолютного пути исходника в путь внутри staging
        rel_path = os.path.relpath(file_path, start=os.getcwd())
        staging_file_path = self.staging_dir / rel_path

        if not staging_file_path.exists():
            return False

        if code == "CS0246": # The type or namespace could not be found
            match = re.search(r"The type or namespace name '(\w+)' could not be found", msg)
            if match:
                missing_type = match.group(1)
                return self._handle_cs0246(staging_file_path, missing_type)
        
        elif code == "CS1503": # Argument cannot convert
            log.warning(f"CS1503 (Requires Context): Небезопасно для автофикса без AST. Файл: {rel_path}")
            return False

        return False

    def _handle_cs0246(self, file_path: Path, missing_type: str) -> bool:
        log.info(f"Анализ отсутствующего типа: {missing_type} в {file_path.name}")
        search_results = self.search.find_type_definition(missing_type)

        if not search_results:
            log.warning(f"Тип {missing_type} не найден в reference репозиториях.")
            return False

        # Приоритет: сначала ищем upstream (может просто нужен using), затем остальные (порт)
        if "upstream" in search_results:
            ns = search_results["upstream"]["namespace"]
            if ns:
                return self._add_using(file_path, ns, missing_type)
        
        # Если нет в upstream, но есть в других - пытаемся портировать файл
        for repo in ["genesis", "goob", "sunrise", "deadspace"]:
            if repo in search_results:
                return self._port_legacy_file(search_results[repo], missing_type)

        return False

    def _add_using(self, file_path: Path, namespace: str, type_name: str) -> bool:
        with open(file_path, 'r', encoding='utf-8') as f:
            lines = f.readlines()
        
        using_str = f"using {namespace};\n"
        if any(using_str.strip() in line for line in lines):
            return False # Уже есть

        # Вставляем после последнего using
        last_using_idx = -1
        for i, line in enumerate(lines):
            if line.startswith("using "):
                last_using_idx = i
        
        if last_using_idx != -1:
            lines.insert(last_using_idx + 1, using_str)
        else:
            lines.insert(0, using_str)

        with open(file_path, 'w', encoding='utf-8') as f:
            f.writelines(lines)

        self.fixes_applied.append({"type": "add_using", "file": str(file_path), "namespace": namespace})
        return True

    def _port_legacy_file(self, repo_result, class_name: str) -> bool:
        src_content = repo_result["content"]
        src_namespace = repo_result["namespace"]
        
        # Создаем папку в staging
        dest_dir = self.staging_dir / "Content.Shared" / "_Legacy"
        dest_dir.mkdir(parents=True, exist_ok=True)
        dest_file = dest_dir / f"{class_name}Old.cs"

        if dest_file.exists():
            return False # Уже портировали

        # Простейшая замена namespace на локальный
        new_content = src_content
        if src_namespace:
            new_content = re.sub(rf'namespace\s+{src_namespace}', "namespace Content.Shared._Legacy", new_content)
        
        # Переименование класса во избежание конфликтов (Old)
        new_content = re.sub(rf'\b{class_name}\b', f"{class_name}Old", new_content)

        with open(dest_file, 'w', encoding='utf-8') as f:
            f.write(new_content)

        self.fixes_applied.append({"type": "port_legacy", "file": str(dest_file), "original_class": class_name})
        return True

class RepairEngine:
    def __init__(self, is_dry_run=False, max_iterations=5):
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
            "files_changed": [],
            "status": "failed",
            "failure_reason": None
        }

    def run_ci(self):
        log.info("Запуск в режиме CI (прямая модификация, предполагается работа в ветке auto-repair-fixes)")
        cwd = Path(os.getcwd())
        
        errors, success = DiagnosticParser.run_build(cwd)
        self.report["initial_errors"] = len(errors)

        if success:
            log.info("Ошибок сборки нет.")
            self.report["status"] = "success"
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
                log.info("Не удалось применить новые исправления. Остановка.")
                self.report["failure_reason"] = "NO_PROGRESS"
                break

            new_errors, new_success = DiagnosticParser.run_build(cwd)
            
            if len(new_errors) >= prev_errors_count:
                log.warning("Количество ошибок увеличилось или не изменилось. Откат последних изменений...")
                # В CI мы доверяем Git для отката (так как это отдельная ветка)
                subprocess.run(["git", "checkout", "."], cwd=cwd)
                subprocess.run(["git", "clean", "-fd"], cwd=cwd)
                self.report["failure_reason"] = "MADE_WORSE"
                break

            errors = new_errors
            prev_errors_count = len(errors)
            self.report["iterations"] = iteration

            if new_success:
                log.info("Сборка успешна!")
                self.report["status"] = "success"
                break

        self.report["final_errors"] = len(errors)
        self.report["fixes_applied"] = strategy.fixes_applied
        self.report["finished_at"] = datetime.now().isoformat()
        self.save_report()

    def save_report(self):
        report_path = f"reports/repair_summary_{datetime.now().strftime('%Y%m%d_%H%M%S')}.json"
        with open(report_path, "w", encoding='utf-8') as f:
            json.dump(self.report, f, indent=4, ensure_ascii=False)
        log.info(f"Отчет сохранен в {report_path}")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="SS14 Autonomous PRO Repair Engine (CI Version)")
    parser.add_argument("--dry-run", action="store_true")
    parser.add_argument("--max-iter", type=int, default=5)
    args = parser.parse_args()

    engine = RepairEngine(is_dry_run=args.dry_run, max_iterations=args.max_iter)
    engine.run_ci()
