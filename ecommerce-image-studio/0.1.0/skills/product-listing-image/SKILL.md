---
name: product-listing-image
description: Plan and generate polished ecommerce product images, then edit supplied product images while preserving product truthfulness. Use when creating marketplace hero images, infographics, lifestyle scenes, detail images, or compliant product-image variants.
---

# Product Listing Images

Use the `image_generate` and `image_edit` MCP tools to create a coherent product-image set. Before generating, identify the product, target marketplace, audience, visual goal, mandatory product facts, and prohibited claims.

## Workflow

1. Start with the primary listing image. Keep one accurately represented product prominent, centered, fully visible, and on a clean white or transparent background when marketplace rules require it.
2. Create supporting images for features, dimensions, use context, materials, and packaging. Give each image one communication purpose.
3. For edits, supply the original product image and state exactly what must stay unchanged: product geometry, colorway, logo, text, and included accessories.
4. Inspect every output. Regenerate or edit if it distorts the product, invents accessories, alters branding, adds unreadable text, creates unsafe use, or makes unsupported claims.

## Prompt Pattern

Write prompts in this order: product identity, non-negotiable visual details, composition, background or scene, lighting, camera angle, desired aspect ratio, and exclusions.

Example: "Studio catalog photograph of the exact matte-black insulated bottle shown, upright and fully visible, centered on pure white, soft shadow, diffuse commercial lighting, no hands, no labels changed, no extra accessories, square composition."

For lifestyle images, name the setting and interaction without hiding the product: "The exact product remains unchanged and clearly visible in the foreground; show only realistic use consistent with its instructions."

## Truthfulness Guardrails

- Do not depict features, quantities, certifications, results, or accessories that the seller cannot substantiate.
- Do not change product dimensions, materials, colors, logos, package contents, or compatibility through generation or editing.
- Do not rely on generated text for product claims, dimensions, or legal disclosures; add verified typography in a dedicated design workflow.
- Treat marketplace image policies as the source of truth for the target channel, especially hero-image background and overlay restrictions.