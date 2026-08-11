# Image API quick reference

Use this file for API and model controls supported by the bundled `scripts/image_gen.mjs` CLI.

These parameters describe the Image API and bundled Node CLI surface.

## Scope
- OpenAI-compatible Images and Imagen models use `/v1/images/generations` and `/v1/images/edits`.
- Gemini image models use `models/{model}:generateContent` and currently support text-to-image generation only.
- The built-in `image_gen` tool and the fallback CLI do not expose the same controls.

## Gemini image models

Model IDs starting with `gemini-` are routed to Gemini `generateContent`, not to `/v1/images/generations`. By default the CLI reuses `OPENAI_API_KEY` and `OPENAI_BASE_URL`, so existing saved OpenAI-compatible settings continue to work. Set `GEMINI_API_KEY` or `GOOGLE_API_KEY`, and optionally `GEMINI_BASE_URL` (default when no OpenAI base URL is set: `https://generativelanguage.googleapis.com/v1beta`) to override them. The request contains the prompt as a text part; the CLI extracts returned `inlineData.data` or `inline_data.data` parts and writes the base64 image.

Gemini generation currently accepts exactly one output image (`--n 1`) and does not support the OpenAI-only `size`, `quality`, `background`, `mask`, or `input_fidelity` controls. Use `--provider gemini` to force this protocol when the model ID does not use the `gemini-` prefix.

## Model summary

| Model | Quality | Input fidelity | Resolutions | Recommended use |
| --- | --- | --- | --- | --- |
| `gpt-image-2` | `low`, `medium`, `high`, `auto` | Always high fidelity for image inputs; do not set `input_fidelity` | `auto` or flexible sizes that satisfy the constraints below | Default for new CLI/API workflows: high-quality generation and editing, text-heavy images, photorealism, compositing, identity-sensitive edits, and workflows where fewer retries matter |
| `gpt-image-1.5` | `low`, `medium`, `high`, `auto` | `low`, `high` | `1024x1024`, `1024x1536`, `1536x1024`, `auto` | True transparent-background fallback and backward-compatible workflows |
| `gpt-image-1` | `low`, `medium`, `high`, `auto` | `low`, `high` | `1024x1024`, `1024x1536`, `1536x1024`, `auto` | Legacy compatibility |
| `gpt-image-1-mini` | `low`, `medium`, `high`, `auto` | `low`, `high` | `1024x1024`, `1024x1536`, `1536x1024`, `auto` | Cost-sensitive draft batches and lower-stakes previews |

## gpt-image-2 sizes

`gpt-image-2` accepts `auto` or any `WIDTHxHEIGHT` size that satisfies all constraints:

- Maximum edge length must be less than or equal to `3840px`.
- Both edges must be multiples of `16px`.
- Long edge to short edge ratio must not exceed `3:1`.
- Total pixels must be at least `655,360` and no more than `8,294,400`.

Popular sizes:

| Label | Size | Notes |
| --- | --- | --- |
| Square | `1024x1024` | Typical fast default |
| Landscape | `1536x1024` | Standard landscape |
| Portrait | `1024x1536` | Standard portrait |
| 2K square | `2048x2048` | Larger square output |
| 2K landscape | `2048x1152` | Widescreen output |
| 4K landscape | `3840x2160` | Widescreen 4K output |
| 4K portrait | `2160x3840` | Vertical 4K output |
| Auto | `auto` | Default size |

Square images are typically fastest to generate. For 4K-style output, use `3840x2160` or `2160x3840`.

## Endpoints
- Generate: `POST /v1/images/generations` (`client.images.generate(...)`)
- Edit: `POST /v1/images/edits` (`client.images.edit(...)`)

## Core parameters for GPT Image models
- `prompt`: text prompt
- `model`: image model
- `n`: number of images (1-10)
- `size`: `auto` by default for `gpt-image-2`; flexible `WIDTHxHEIGHT` sizes are allowed only for `gpt-image-2`; older GPT Image models use `1024x1024`, `1536x1024`, `1024x1536`, or `auto`
- `quality`: `low`, `medium`, `high`, or `auto`
- `background`: output transparency behavior (`transparent`, `opaque`, or `auto`) for generated output; this is not the same thing as the prompt's visual scene/backdrop
- `output_format`: `png` (default), `jpeg`, `webp`
- `output_compression`: 0-100 (jpeg/webp only)
- `moderation`: `auto` (default) or `low`

## Edit-specific parameters
- `image`: one or more input images. For GPT Image models, you can provide up to 16 images.
- `mask`: optional mask image
- `input_fidelity`: `low` or `high` only for models that support it; do not set this for `gpt-image-2`

Model-specific note for `input_fidelity`:
- `gpt-image-2` always uses high fidelity for image inputs and does not support setting `input_fidelity`.
- `gpt-image-1` and `gpt-image-1-mini` preserve all input images, but the first image gets richer textures and finer details.
- `gpt-image-1.5` preserves the first 5 input images with higher fidelity.

## Transparent backgrounds

`gpt-image-2` does not currently support the Image API `background=transparent` parameter. Generate a flat chroma-key background, then extract alpha with `%USERPROFILE%\.codex\skills\noderuntime\scripts\run-node.cmd "%USERPROFILE%\.codex\skills\imagegenauto\scripts\remove_chroma_key.mjs"`.

Use `gpt-image-1.5` with `background=transparent` and a transparent-capable output format such as `png` or `webp` when native transparency is required. This is preferable for subjects that are too complex for clean chroma-key removal, such as hair, glass, smoke, liquids, or soft shadows.

## Output
- `data[]` list with `b64_json` per image
- The bundled `scripts/image_gen.mjs` CLI decodes `b64_json` and writes output files for you.

## Limits and notes
- Input images and masks must be under 50MB.
- Use the edits endpoint when the user requests changes to an existing image.
- Masking is prompt-guided; exact shapes are not guaranteed.
- Large sizes and high quality increase latency and cost.
- Use `quality=low` for fast drafts, thumbnails, and quick iterations. Use `medium` or `high` for final assets, dense text, diagrams, identity-sensitive edits, or high-resolution outputs.
- High `input_fidelity` can materially increase input token usage on models that support it.
- If a request fails because a specific option is unsupported by the selected GPT Image model, retry manually without that option only when the option is not required by the user. If true transparent CLI output is required, ask before switching to `gpt-image-1.5` instead of dropping `background=transparent`, unless the user already explicitly chose that fallback.

## Important boundary
- `quality`, `input_fidelity`, explicit masks, `background`, `output_format`, and related parameters are CLI execution controls.
