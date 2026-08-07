# Node CLI Reference

All commands target Windows x64 and use the managed Node launcher:

```bat
set "CODEX_HOME=%USERPROFILE%\.codex"
set "NODE_RUN=%CODEX_HOME%\skills\noderuntime\scripts\run-node.cmd"
set "IMAGE_GEN=%CODEX_HOME%\skills\imagegenauto\scripts\image_gen_auto.mjs"
```

Live calls require `OPENAI_API_KEY` and network access. `OPENAI_BASE_URL` is optional. Use `--dry-run` to inspect payloads without credentials or network.

## Generate

```bat
"%NODE_RUN%" "%IMAGE_GEN%" generate --prompt "A cozy alpine cabin at dawn" --size 1024x1024 --quality high --out "output\imagegen\alpine-cabin.png"
```

Defaults: model `gpt-image-2`, size `auto`, quality `medium`, format `png`, count `1`.

Useful flags: `--model`, `--prompt-file`, `--n`, `--size`, `--quality`, `--background`, `--output-format`, `--output-compression`, `--moderation`, `--force`, `--dry-run`, and `--no-augment`.

Prompt fields: `--use-case`, `--scene`, `--subject`, `--style`, `--composition`, `--lighting`, `--palette`, `--materials`, `--text`, `--constraints`, and `--negative`.

## Edit

```bat
"%NODE_RUN%" "%IMAGE_GEN%" edit --image "input.png" --prompt "Replace only the background; preserve the product" --out "output\imagegen\edited.png"
```

Pass repeated `--image` flags for multiple inputs. `--mask` accepts one mask. `--input-fidelity low|high` is available on supporting models; omit it for `gpt-image-2`.

## Batch

Use JSONL with one object per distinct asset:

```jsonl
{"prompt":"A clean catalog photograph of a matte mug","size":"1024x1024"}
{"prompt":"The same mug in a bright kitchen scene","size":"1536x1024","n":2}
```

Run the core CLI because retries occur per command rather than per batch job:

```bat
"%NODE_RUN%" "%CODEX_HOME%\skills\imagegenauto\scripts\image_gen.mjs" generate-batch --input "tmp\imagegen\prompts.jsonl" --out-dir "output\imagegen\batch"
```

Outputs are numbered by job, such as `image_1.png`, `image_2-1.png`, and `image_2-2.png`. JSONL fields override command defaults.

## Downscaling

Add `--downscale-max-dim 1024` to write a second copy with the default `-web` suffix. Override it with `--downscale-suffix`. The shared `sharp` package is installed by the managed runtime.

## Transparency

Native alpha:

```bat
"%NODE_RUN%" "%IMAGE_GEN%" generate --model gpt-image-1.5 --prompt "A clean product cutout" --background transparent --output-format png --out "output\imagegen\cutout.png"
```

Local chroma-key removal:

```bat
"%NODE_RUN%" "%CODEX_HOME%\skills\imagegenauto\scripts\remove_chroma_key.mjs" --input "tmp\imagegen\green.png" --out "output\imagegen\cutout.png" --auto-key border --soft-matte --despill
```

The removal CLI also supports `--key-color`, `--tolerance`, `--transparent-threshold`, `--opaque-threshold`, `--edge-contract`, `--edge-feather`, and `--force`.