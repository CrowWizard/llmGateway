---
name: product-listing-image
description: Plan and generate polished ecommerce product images, then edit supplied product images while preserving product truthfulness. Use when creating marketplace hero images, infographics, lifestyle scenes, detail images, or compliant product-image variants.
---

# Product Listing Images

Use the installed `imagegenauto` Skill to create a coherent product-image set. Before generating, identify the product, target marketplace, audience, visual goal, mandatory product facts, and prohibited claims. Select the image protocol before execution: use an OpenAI-compatible Images/Imagen model for reference-image edits, and use a Gemini image model only for text-to-image generation.

## Core Rules

- Do not invent product facts, features, certifications, dimensions, materials, package contents, brand assets, or claims.
- For edits, preservation requirements take priority over creative direction. State what must remain unchanged before describing the requested change.
- Inspect every supplied image before editing. Do not infer hidden or occluded product details.
- Ask a concise clarifying question when the requested outcome is ambiguous.
- Use `image_edit` whenever a product reference image is supplied; use `image_generate` only for a new image without a reference. Gemini models currently support `image_generate` only; when a reference image is supplied, select an OpenAI-compatible Images or Imagen model instead.
- Keep each output image focused on one selling purpose. Use verified typography in a dedicated design workflow rather than relying on generated text for legal claims or dimensions.

## Reference Loading Contract

The detailed instructions in `references/` are required task guidance. Before choosing a tool or writing a prompt, load every relevant reference below.

| Request | Load |
|---|---|
| Marketplace main image, listing image set, or batch | `references/platform-product-guidelines.md` and the selected output-scene references |
| Pure white or transparent background | `references/white-background.md` |
| Lifestyle, use-context, or background replacement | `references/scene-image.md` |
| Product recolor or SKU variant | `references/sku-color-change.md` |
| Detail close-up | `references/image-detail.md` |
| Feature, comparison, callout, or marketing layout | `references/selling-point.md` |
| Model wearing, holding, or using the product | `references/model-showcase.md` |
| Existing logo applied to a product | `references/logo-customization.md` |
| New logo | `references/logo-design.md` |
| Text correction, translation, or approved overlay removal | `references/text-editing.md`, `references/image-translation.md`, or `references/remove-watermark.md` as appropriate |
| Resize, restore, or upscale | `references/image-resize.md` or `references/hd-upscale.md` |
| OEM drawing or manufacturing process graphic | `references/tech-pack.md` or `references/process-flow.md` |
| General visual styling | `references/style-guide.md` |

For a marketplace image set, plan the requested number of outputs before generating: hero image, scene image, detail image, selling-point image, scale/use image, and packaging/accessory image. Only include claims and accessories that are visible or confirmed by the seller.

## Workflow

1. Start with the primary listing image. Keep one accurately represented product prominent, centered, fully visible, and on a clean white or transparent background when marketplace rules require it.
2. Create supporting images for features, dimensions, use context, materials, and packaging. Give each image one communication purpose.
3. For edits, supply the original product image and state exactly what must stay unchanged: product geometry, colorway, logo, text, and included accessories.
4. Inspect every output. Regenerate or edit if it distorts the product, invents accessories, alters branding, adds unreadable text, creates unsafe use, or makes unsupported claims.

## Execution

This is a regular Skill, not an MCP plugin. It targets Windows x64 and invokes the shared managed Node runtime without PowerShell:

```bat
set "CODEX_HOME=%USERPROFILE%\.codex"
set "NODE_RUN=%CODEX_HOME%\skills\noderuntime\scripts\run-node.cmd"
set "IMAGE_GEN=%CODEX_HOME%\skills\imagegenauto\scripts\image_gen_auto.mjs"
```

Generate a new listing image with `"%NODE_RUN%" "%IMAGE_GEN%" generate ...`. When a product reference exists, use `edit --image <path> ...` so product identity and geometry can be preserved. Save final assets under the current project's `output/imagegen/` directory.

For OpenAI-compatible Images and Imagen models, configure `OPENAI_API_KEY`, with optional `OPENAI_BASE_URL` and `OPENAI_IMAGE_MODEL`. Gemini text-to-image generation reuses `OPENAI_API_KEY` and `OPENAI_BASE_URL` by default; `GEMINI_API_KEY` (or `GOOGLE_API_KEY`) and `GEMINI_BASE_URL` override those values when needed. Choose a model whose ID starts with `gemini-`. Gemini generation returns one image per request and does not support `image_edit`. Never request a secret in chat. Do not invoke npm, Python, or PowerShell.

### Model selection

- New image with no product reference: `gemini-*-image-*` may use Gemini `generateContent`; `imagen-*` and `gpt-image-*` use the OpenAI-compatible Images endpoint.
- Product image edit, recolor, background replacement, logo application, or any task with `--image`: use an OpenAI-compatible Images or Imagen model because the current Gemini adapter is generation-only.
- Keep the same provider and model family across a listing set when product fidelity and visual consistency matter. Do not silently replace a requested Gemini model with an OpenAI or Imagen model; report the protocol limitation and select a compatible model.

## Prompt Pattern

Write prompts in this order: product identity, non-negotiable visual details, composition, background or scene, lighting, camera angle, desired aspect ratio, and exclusions.

Example: "Studio catalog photograph of the exact matte-black insulated bottle shown, upright and fully visible, centered on pure white, soft shadow, diffuse commercial lighting, no hands, no labels changed, no extra accessories, square composition."

For lifestyle images, name the setting and interaction without hiding the product: "The exact product remains unchanged and clearly visible in the foreground; show only realistic use consistent with its instructions."

## Truthfulness Guardrails

- Do not depict features, quantities, certifications, results, or accessories that the seller cannot substantiate.
- Do not change product dimensions, materials, colors, logos, package contents, or compatibility through generation or editing.
- Do not rely on generated text for product claims, dimensions, or legal disclosures; add verified typography in a dedicated design workflow.
- Treat marketplace image policies as the source of truth for the target channel, especially hero-image background and overlay restrictions.
