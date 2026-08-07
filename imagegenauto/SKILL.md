---
name: imagegenauto
description: Generate, edit, batch-generate, and post-process raster images on Windows x64 with the managed Node.js runtime and OpenAI-compatible Images APIs.
---

# Image Generation

Use this Skill for bitmap generation, image edits, variants, ecommerce imagery, mockups, illustrations, and transparent-background assets. It requires `OPENAI_API_KEY`; `OPENAI_BASE_URL` may point to a compatible provider, and `OPENAI_IMAGE_MODEL` may override the default model.

## Runtime

This installation supports Windows x64. Always run scripts through the shared launcher:

```bat
set "CODEX_HOME=%USERPROFILE%\.codex"
set "IMAGEGEN=%CODEX_HOME%\skills\imagegenauto\scripts\image_gen_auto.mjs"
"%CODEX_HOME%\skills\noderuntime\scripts\run-node.cmd" "%IMAGEGEN%" generate --prompt "A catalog product photograph" --out "output\imagegen\product.png"
```

The launcher uses managed Node.js first and system `node.exe` only as a fallback. Do not invoke PowerShell, Python, pip, npm, or install dependencies from this Skill.

## Commands

- `generate`: create an image from a prompt.
- `edit`: edit one or more local images; pass repeated `--image` values and optional `--mask`.
- `generate-batch`: read JSONL jobs from `--input` and write unique outputs under `--out-dir`.

Use `scripts/image_gen_auto.mjs` for generation and edits because it retries transient failures and falls back from `gpt-image-2` to `gpt-image-1.5`, then `gpt-image-1`. An explicit `--model` disables model fallback. Use `scripts/image_gen.mjs` directly for `generate-batch`.

Save project deliverables under `output/imagegen/` unless the user provides another path. Do not overwrite files unless replacement was requested; only then pass `--force`. Verify every reported output exists and is non-empty.

## Prompting

Preserve the user's primary request. Add only useful fields such as `--use-case`, `--scene`, `--subject`, `--style`, `--composition`, `--lighting`, `--palette`, `--materials`, `--text`, `--constraints`, and `--negative`. For edits, state exact invariants and use `--image` rather than generation without a reference.

Load `references/prompting.md` and relevant examples from `references/sample-prompts.md`. Load `references/cli.md` for command details and `references/image-api.md` for provider parameter constraints.

## Transparent Images

For native transparency, use `gpt-image-1.5 --background transparent --output-format png`; `gpt-image-2` does not support transparent output. For a simple opaque subject, generation on a flat chroma-key background followed by local removal is also supported:

```bat
"%CODEX_HOME%\skills\noderuntime\scripts\run-node.cmd" "%CODEX_HOME%\skills\imagegenauto\scripts\remove_chroma_key.mjs" --input "tmp\source.png" --out "output\imagegen\cutout.png" --auto-key border --soft-matte --despill
```

Use chroma key only for subjects with clean opaque edges. Prefer native transparency for hair, fur, smoke, glass, liquids, translucent materials, reflections, or soft shadows.

## Validation

Inspect subject accuracy, composition, text, requested invariants, output dimensions, and transparency. For transparent assets, confirm an alpha channel and transparent corners. Retry with one targeted prompt change rather than broad rewrites.