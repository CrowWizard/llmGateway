# Ecommerce Image Studio

A local Codex plugin that exposes `image_generate` and `image_edit` through an OpenAI Images API-compatible provider.

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

## Validate

```powershell
npm run validate
```

The validator checks Node version, required plugin assets, JSON syntax, and MCP registration.
