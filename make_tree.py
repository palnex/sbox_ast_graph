import os
import sys
import fnmatch

try:
    import pathspec
    HAS_PATHSPEC = True
except ImportError:
    HAS_PATHSPEC = False

# =====================================================================
# 🛠 КАСТОМНІ ПРАВИЛА ІГНОРУВАННЯ (VS Code files.exclude / .gitignore)
# Сюди можна кидати будь-які паттерни, які ти НЕ хочеш показувати ШІ
# =====================================================================
CUSTOM_EXCLUDES = [
    # Твої настройки з VS Code:
    "**/bin",
    "**/obj",
    "**/output_test",
    "**/temp_api_stub.cs",
    "**/api.json",
    "**/.obsidian/workspace.json",
    "**/.obsidian/graph.json",
    "**/.obsidian/workspace-mobile.json",
    "**/.obsidian",
    
    # Твої додаткові правила з гіту та Python/WSL:
    "bin/",
    "obj/",
    "output_test/",
    "temp_api_stub.cs",
    "api.json",
    "__pycache__/",
    "*.py[cod]",
    "*$py.class",
    ".venv*",
    "venv*",
    ".DS_Store",
    "Thumbs.db",
    "*-----OLD/",
]

# Системні файли скрипта
ALWAYS_IGNORE = {'.git', '.idea', '.vscode', 'project_structure.md', 'make_tree.py'}


def load_all_ignores(root_dir):
    """Збирає разом .gitignore + CUSTOM_EXCLUDES + ALWAYS_IGNORE"""
    patterns = set(ALWAYS_IGNORE)
    patterns.update(CUSTOM_EXCLUDES)

    # Читаємо локальний .gitignore, якщо він є
    gitignore_path = os.path.join(root_dir, '.gitignore')
    if os.path.exists(gitignore_path):
        with open(gitignore_path, 'r', encoding='utf-8', errors='ignore') as f:
            for line in f:
                line = line.strip()
                if line and not line.startswith('#'):
                    patterns.add(line)

    if HAS_PATHSPEC:
        return pathspec.PathSpec.from_lines('gitwildmatch', list(patterns))
    return list(patterns)


def is_ignored(rel_path, spec):
    """Перевіряє, чи ігнорувати файл/папку"""
    name = os.path.basename(rel_path)
    if name in ALWAYS_IGNORE:
        return True

    posix_path = rel_path.replace(os.sep, '/')

    if HAS_PATHSPEC:
        # Для папок pathspec вимагає слеш в кінці, щоб правильно спрацювали правила типу "bin/"
        return spec.match_file(posix_path) or spec.match_file(posix_path + '/')
    else:
        # Резервний механізм (якщо немає бібліотеки pathspec)
        for pattern in spec:
            clean_pattern = pattern.replace('**/', '').rstrip('/')
            if fnmatch.fnmatch(posix_path, clean_pattern) or fnmatch.fnmatch(name, clean_pattern):
                return True
        return False


def get_file_info(filepath):
    """Повертає розмір і кількість рядків"""
    try:
        size = os.path.getsize(filepath)
        if size < 1024:
            size_str = f"{size}B"
        elif size < 1024 * 1024:
            size_str = f"{size/1024:.1f}KB"
        else:
            size_str = f"{size/(1024*1024):.1f}MB"

        # Перевірка на бінарник
        with open(filepath, 'rb') as f:
            if b'\0' in f.read(1024):
                return f"BIN, {size_str}"

        with open(filepath, 'r', encoding='utf-8', errors='ignore') as f:
            lines = sum(1 for _ in f)

        return f"{lines}L, {size_str}"
    except Exception:
        return "N/A"


def generate_tree(root_dir="."):
    spec = load_all_ignores(root_dir)
    output = ["# Project Structure\n"]

    root_dir = os.path.abspath(root_dir)

    for root, dirs, files in os.walk(root_dir):
        rel_root = os.path.relpath(root, root_dir)
        if rel_root == ".":
            rel_root = ""

        # Відсікаємо ігноровані папки ДО того, як у них зайти
        dirs[:] = [
            d for d in dirs
            if not is_ignored(os.path.join(rel_root, d), spec)
        ]

        dirs.sort()
        files.sort()

        level = 0 if rel_root == "" else rel_root.count(os.sep) + 1
        indent = "  " * level

        if rel_root != "":
            folder_name = os.path.basename(root)
            output.append(f"{indent}- **{folder_name}/**")
            indent += "  "

        for f in files:
            rel_file_path = os.path.join(rel_root, f)
            if is_ignored(rel_file_path, spec):
                continue

            full_path = os.path.join(root, f)
            info = get_file_info(full_path)
            output.append(f"{indent}- `{f}` `({info})`")

    return "\n".join(output)


if __name__ == "__main__":
    tree_md = generate_tree(".")

    output_filename = "project_structure.md"
    with open(output_filename, "w", encoding="utf-8") as f:
        f.write(tree_md)

    print(f"✅ Готово! Структуру з усіма фільтрами збережено в: {output_filename}")