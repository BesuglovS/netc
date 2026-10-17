#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
Конвертер квизов проекта python-web (C:\\websites\\na\\python-web\\quizzes)
в файлы тестов для комплекса netc (формат TestDefinition, импорт в окне «Тест»).

Запуск:  python convert_quizzes.py
Вход:    C:\\websites\\na\\python-web\\quizzes\\*.json   (1.json … 50.json, final-test.json)
         C:\\websites\\na\\python-web\\lessons.json       (названия уроков)
Выход:   эта же папка — urok-01.json … urok-50.json, itogovyy.json
"""

import html
import json
import re
import uuid
from pathlib import Path

QUIZ_DIR = Path(r"C:\websites\na\python-web\quizzes")
LESSONS = Path(r"C:\websites\na\python-web\lessons.json")
OUT_DIR = Path(__file__).resolve().parent

# Значения по умолчанию для сгенерированных тестов (меняются в редакторе клиента).
DURATION_SECONDS = 0      # 0 — без ограничения по времени
SHUFFLE_QUESTIONS = True
SHUFFLE_OPTIONS = True
SHOW_RESULT = True
PASS_PERCENT = 70

TAG_RE = re.compile(r"<[^>]+>")


def clean(text: str) -> str:
    """Убирает HTML-теги и раскрывает сущности: <code>x</code> -> x, &lt; -> <."""
    if not text:
        return ""
    t = TAG_RE.sub("", text)
    t = t.replace("&nbsp;", " ")
    t = html.unescape(t)  # безопасно: &amp; раскроется последним
    return re.sub(r"\s+", " ", t).strip()


def lesson_titles() -> dict:
    if not LESSONS.exists():
        return {}
    data = json.loads(LESSONS.read_text(encoding="utf-8"))
    titles = {}
    for section in data.get("sections", []):
        for lesson in section.get("lessons", []):
            titles[int(lesson["num"])] = lesson.get("title", "").strip()
    return titles


def convert(quiz_path: Path, title: str, rel_key: str) -> tuple:
    raw = json.loads(quiz_path.read_text(encoding="utf-8"))
    questions, skipped = [], 0

    for item in raw:
        q_text = clean(item.get("question", ""))
        options = [clean(o) for o in item.get("options", [])]
        correct = item.get("correct")
        if not q_text or len(options) < 2 or not isinstance(correct, int) or not (0 <= correct < len(options)):
            skipped += 1
            continue
        questions.append({
            "text": q_text,
            "kind": 0,  # QuestionKind.Single
            "options": options,
            "correctIndex": correct,
            "points": 1,
            "explanation": clean(item.get("explanation", "")),
        })

    if not questions:
        return None, skipped

    test = {
        "id": str(uuid.uuid5(uuid.NAMESPACE_URL, "na-python-web/" + rel_key)),
        "title": title,
        "questions": questions,
        "durationSeconds": DURATION_SECONDS,
        "shuffleQuestions": SHUFFLE_QUESTIONS,
        "shuffleOptions": SHUFFLE_OPTIONS,
        "showResultToStudent": SHOW_RESULT,
        "passPercent": PASS_PERCENT,
        "escapePin": "",
    }
    return test, skipped


def main() -> int:
    titles = lesson_titles()
    total_q = total_files = skipped_all = 0

    # Уроки 1..50 (имя файла — номер без ведущего нуля).
    for num in range(1, 51):
        src = QUIZ_DIR / f"{num}.json"
        if not src.exists():
            print(f"  пропуск: нет {src.name}")
            continue
        lesson = titles.get(num, "")
        title = f"Урок {num}. {lesson}".strip().rstrip(".")
        test, skipped = convert(src, title, f"lesson-{num}")
        skipped_all += skipped
        if test is None:
            print(f"  ПРОПУСК ({src.name}): нет корректных вопросов")
            continue
        out = OUT_DIR / f"urok-{num:02d}.json"
        out.write_text(json.dumps(test, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
        total_files += 1
        total_q += len(test["questions"])
        extra = f", пропущено {skipped}" if skipped else ""
        print(f"  {out.name}: {len(test['questions'])} вопр.{extra}  «{title}»")

    # Итоговый тест.
    src = QUIZ_DIR / "final-test.json"
    if src.exists():
        course = ""
        if LESSONS.exists():
            course = json.loads(LESSONS.read_text(encoding="utf-8")).get("title", "").strip()
        title = f"Итоговый тест — {course}".strip(" —")
        test, skipped = convert(src, title, "final-test")
        skipped_all += skipped
        if test is not None:
            out = OUT_DIR / "itogovyy.json"
            out.write_text(json.dumps(test, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
            total_files += 1
            total_q += len(test["questions"])
            extra = f", пропущено {skipped}" if skipped else ""
            print(f"  {out.name}: {len(test['questions'])} вопр.{extra}  «{title}»")

    print()
    print(f"Готово: файлов {total_files}, вопросов {total_q}, пропущено {skipped_all}")
    print(f"Папка: {OUT_DIR}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
