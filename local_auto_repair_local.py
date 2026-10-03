#!/usr/bin/env python3
import os
import shutil
import argparse
import logging
from pathlib import Path
import subprocess
from auto_repair import RepairEngine, DiagnosticParser, FixStrategy, GitManager

log = logging.getLogger("SS14RepairLocal")
logging.basicConfig(level=logging.INFO, format='%(levelname)s: %(message)s')

class LocalStagingManager:
    def __init__(self, root_dir: str):
        self.root = Path(root_dir).resolve()
        self.staging = self.root / "auto-repair-fixes" / "staging"
        self.git = GitManager(str(self.root))

    def create_staging(self):
        if self.staging.exists():
            log.info("Очистка старой staging-директории...")
            shutil.rmtree(self.staging, ignore_errors=True)
            
        log.info(f"Создание staging-копии в {self.staging}...")
        
        def ignore_patterns(path, names):
            return {'.git', 'bin', 'obj', '.repair_cache', 'auto-repair-fixes', 'logs', 'reports'}
            
        shutil.copytree(self.root, self.staging, ignore=ignore_patterns)

    def generate_diff(self, original_file: Path, modified_file: Path):
        with open(original_file, 'r', encoding='utf-8') as f1, \
             open(modified_file, 'r', encoding='utf-8') as f2:
            import difflib
            diff = difflib.unified_diff(
                f1.readlines(), f2.readlines(),
                fromfile=original_file.name, tofile=modified_file.name
            )
            return "".join(diff)

    def run_local_repair(self, max_iter=5, dry_run=False):
        if self.git.is_dirty():
            log.warning("В репозитории есть незакоммиченные изменения. Они будут включены в staging, но будьте осторожны.")

        # 1. Baseline build in Root
        log.info("Проверка состояния основного репозитория (baseline)...")
        root_errors, root_success = DiagnosticParser.run_build(self.root)
        if root_success:
            log.info("Основной репозиторий собирается без ошибок. Ремонт не требуется.")
            return

        # 2. Setup Staging
        self.create_staging()
        engine = RepairEngine(is_dry_run=dry_run, max_iterations=max_iter)
        strategy = FixStrategy(engine.search, self.staging)

        # 3. Repair Loop inside Staging
        current_errors = root_errors
        prev_error_count = len(current_errors)
        
        for iteration in range(1, max_iter + 1):
            fixes_applied_now = False
            for err in current_errors:
                # Пытаемся починить. Исправления пишутся сразу в staging-копию
                if strategy.attempt_fix(err):
                    fixes_applied_now = True

            if not fixes_applied_now:
                log.info("Больше нет применимых безопасных автофиксов.")
                break

            # 4. Проверка сбороки в staging
            new_errors, success = DiagnosticParser.run_build(self.staging)
            
            if len(new_errors) >= prev_error_count:
                log.error("Вмешательство ухудшило или не изменило ситуацию. Остановка.")
                break

            current_errors = new_errors
            prev_error_count = len(current_errors)

            if success:
                log.info("Сборка в staging УСПЕШНА!")
                break

        # 5. Анализ результатов и применение
        if len(strategy.fixes_applied) > 0 and prev_error_count < len(root_errors):
            log.info("Есть подтвержденные улучшения.")
            
            if dry_run:
                log.info("DRY-RUN: Изменения не будут перенесены в основной репозиторий.")
                return

            # Создаем бекап перед модификацией корня
            backup_branch = self.git.create_backup_branch()
            log.info(f"Основной код защищен веткой {backup_branch}.")

            # Переносим ТОЛЬКО измененные/новые файлы
            for fix in strategy.fixes_applied:
                staging_file = Path(fix['file'])
                rel_path = staging_file.relative_to(self.staging)
                root_file = self.root / rel_path

                root_file.parent.mkdir(parents=True, exist_ok=True)
                
                if root_file.exists():
                    log.info(f"Diff для {rel_path}:\n{self.generate_diff(root_file, staging_file)}")
                
                shutil.copy2(staging_file, root_file)
                log.info(f"Применен патч к {rel_path}")

            # 6. Финальная проверка в основном репозитории
            final_errs, final_success = DiagnosticParser.run_build(self.root)
            if len(final_errs) >= len(root_errors):
                log.error("КРИТИЧЕСКИЙ СБОЙ: Сборка в корне провалилась после патча!")
                log.error(f"Откатитесь с помощью: git reset --hard {backup_branch}")
            else:
                log.info("УСПЕХ: Изменения успешно интегрированы в рабочий репозиторий!")
        else:
            log.info("Не удалось добиться улучшений. Основной репозиторий не затронут.")

if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Локальный безопасный запуск Repair Engine")
    parser.add_argument("--dry-run", action="store_true", help="Не применять изменения в корень")
    parser.add_argument("--max-iter", type=int, default=5)
    args = parser.parse_args()

    manager = LocalStagingManager(os.getcwd())
    manager.run_local_repair(max_iter=args.max_iter, dry_run=args.dry_run)
