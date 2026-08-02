"""Completion-first wrapper for the bundled image generation CLI."""

import argparse
import subprocess
import sys
import time
from pathlib import Path


MODELS = ("gpt-image-2", "gpt-image-1.5", "gpt-image-1")
TRANSIENT_MARKERS = ("503", "server error", "temporar", "timeout", "connection", "unavailable")


def value_after(arguments: list[str], flag: str) -> str | None:
    for index, argument in enumerate(arguments[:-1]):
        if argument == flag:
            return arguments[index + 1]
    return None


def remove_model(arguments: list[str]) -> list[str]:
    cleaned: list[str] = []
    skip_next = False
    for argument in arguments:
        if skip_next:
            skip_next = False
        elif argument == "--model":
            skip_next = True
        else:
            cleaned.append(argument)
    return cleaned


def main() -> int:
    parser = argparse.ArgumentParser(description="Retry and fall back across GPT Image models")
    parser.add_argument("command", choices=("generate", "edit"))
    parser.add_argument("arguments", nargs=argparse.REMAINDER)
    parsed = parser.parse_args()

    original_model = value_after(parsed.arguments, "--model")
    models = (original_model,) if original_model else MODELS
    base_arguments = remove_model(parsed.arguments)
    output = value_after(base_arguments, "--out")
    cli = Path(__file__).with_name("image_gen.py")

    for model_index, model in enumerate(models):
        for attempt in range(1, 4):
            command = [sys.executable, str(cli), parsed.command, "--model", model, *base_arguments]
            result = subprocess.run(command, text=True, capture_output=True)
            is_dry_run = "--dry-run" in base_arguments
            if result.returncode == 0 and (
                is_dry_run or not output or Path(output).is_file() and Path(output).stat().st_size > 0
            ):
                sys.stdout.write(result.stdout)
                return 0

            error = (result.stderr + result.stdout).lower()
            if original_model or not any(marker in error for marker in TRANSIENT_MARKERS):
                sys.stderr.write(result.stderr or result.stdout)
                return result.returncode or 1
            if attempt < 3:
                time.sleep(min(8, 2**attempt))
        if model_index < len(models) - 1:
            print(f"{model} unavailable; trying {models[model_index + 1]}", file=sys.stderr)

    print("All configured GPT Image models failed after retries.", file=sys.stderr)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
