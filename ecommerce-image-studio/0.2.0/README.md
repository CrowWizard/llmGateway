# Ecommerce Image Studio

A local Codex plugin that exposes `health_check`, `image_generate`, and `image_edit` through an OpenAI Images API-compatible provider.

## Requirements

- Node.js 20 or newer. This plugin has no npm dependencies.
- An HTTPS OpenAI-compatible Images API endpoint and API key. The default model is `gpt-image-2`.

## Configure

The MCP server reads credentials only from its process environment. It first uses the generic compatible-provider variables, then falls back to standard OpenAI variable names:

```powershell
$env:EIS_PROVIDER_API_KEY = "..."
$env:EIS_PROVIDER_BASE_URL = "https://your-compatible-provider.example/v1"
$env:EIS_PROVIDER_MODEL = "gpt-image-2"
```

When `EIS_PROVIDER_*` is absent, set `OPENAI_API_KEY` and optionally `OPENAI_BASE_URL` and `OPENAI_IMAGE_MODEL`. The default endpoint is `https://api.openai.com/v1`; the default model is `gpt-image-2`. Supported models are `gpt-image-2`, `gpt-image-2-1.5k`, `gpt-image-1.5`, and `gpt-image-1`. Restart Codex after setting persistent environment variables so the MCP server inherits them.

## v0.2 workflow

1. Call `health_check` before a production batch. It verifies local provider configuration without generating an image.
2. Pass `output_path` to `image_generate` or `image_edit` to save the returned PNG locally as well as displaying it in Codex.
3. Use `input_fidelity: "high"` with `gpt-image-1.5` edits when preserving a supplied product is critical. Omit it for `gpt-image-2`, which already preserves inputs at high fidelity.
4. If the provider returns a `503` for `gpt-image-2`, the tool tells Codex to request the user's confirmation before retrying with `gpt-image-1.5`. It never silently downgrades.
5. `gpt-image-2` transparent output is rejected before the API call. Confirm with the user before explicitly using `gpt-image-1.5` plus `background: "transparent"`.

## Validate

```powershell
npm run validate
```

The validator checks Node version, required plugin assets, JSON syntax, and MCP registration.
