#!/usr/bin/env bash
# afterFileEdit hook: run csharpier on the edited file when it's a .cs file.
# Fails open so style noise can never block a write.

set -u

input=$(cat)

# Cursor's afterFileEdit payload puts the absolute path in `.file_path`.
# Fall back to common alternates so the hook keeps working if the schema shifts.
file_path=$(printf '%s' "$input" | jq -r '
  .file_path
  // .tool_input.file_path
  // .target_file
  // .tool_input.target_file
  // empty
' 2>/dev/null)

if [[ -z "$file_path" ]]; then
  exit 0
fi

case "$file_path" in
  *.cs) ;;
  *) exit 0 ;;
esac

# Skip generated / build output paths Csharpier shouldn't touch.
case "$file_path" in
  *.godot/*|*/obj/*|*/bin/*) exit 0 ;;
esac

cd "$(dirname "$0")/../.." || exit 0

if ! command -v dotnet >/dev/null 2>&1; then
  exit 0
fi

dotnet csharpier "$file_path" >/dev/null 2>&1 || true
exit 0
