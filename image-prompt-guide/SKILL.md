---
name: image-prompt-guide
description: >-
  Prompt engineering and tool routing for AI image generation and editing.
  Use for: creative generation, product photo editing, e-commerce platform image sets,
  and specialized scenes (white background, authorized watermark/element cleanup, HD upscale, image resize, scene swap,
  model showcase, selling point, logo design, tech pack, process flowchart, etc.).
  Do NOT use for: full product development workflows (use ai-product-designer),
  or non-AI operations like crop/compress/format-convert (use native tools).
enabled: true
---

# Image Generation Guide

## Core Rules

| # | Rule | Description |
|---|------|-------------|
| 1 | **No Hallucination** | Do not fabricate product facts, selling points, certifications, dimensions, materials, brand assets, text, labels, hidden details, or unsupported claims. Visual execution choices such as lighting, composition, clean background, natural shadow, camera angle, whitespace, and style may be inferred when they do not change the product meaning or introduce new factual claims. After constructing a prompt, verify that all factual instructions trace back to the user request, visible image evidence, or confirmed platform requirements. |
| 2 | **Clarify Before Guessing** | Ask for clarification whenever the editing intent is ambiguous, including safe default requests like "make it cleaner/professional", "fix this image", or "optimize this photo". Do not proceed with conservative visual cleanup without confirming what the user wants. |
| 3 | **Preserve-First Editing** | When editing, stating what NOT to change is more important than what to change. Every edit prompt must include a preservation clause listing elements that must remain untouched. |
| 4 | **Be Specific** | Define subject, environment, lighting, mood, and style explicitly. Replace vague terms ("beautiful", "professional") with concrete visual descriptors. Use natural sentences for describing intent; comma-separated keywords are acceptable for style/quality modifiers (e.g., "8K, hyperrealistic, sharp detail"). |
| 5 | **Incremental Refinement** | When the user requests a follow-up change to a previous result, apply targeted edits rather than regenerating from scratch. Preserve what already works, fix only what the user calls out. |
| 6 | **No Brand Infringement** | Do not include recognizable brand logos, names, or trademarked elements unless the user explicitly requests their own brand assets. For logo design, see `references/logo-design.md` for detailed anti-infringement rules. |

---

## Reference Loading Contract

References are part of the required workflow, not optional background reading.

Before constructing a prompt, choosing a final `task_type`, or calling any image tool:

1. Match the request to the Scene Router.
2. Load every matched scene reference listed in the router table.
3. **If the request is a batch generation** (multiple images + same operation), also load `references/platform-product-guidelines.md` for the Batch Generation rules, on top of the matched output scene references.
4. For composite workflows, load only the composite reference plus the references for the selected output scenes.
5. Apply the loaded reference's prompt template, hard constraints, safety boundaries, and tool invocation rules.
6. If a request matches multiple scenes for the same output image, read each matched reference, then decide whether to merge them into one call using Step 4.

Do not skip a reference because the router table appears sufficient. The table is only an index; the detailed rules live in `references/`.

> **Platform reference loading rule**: Only load `references/platform-product-guidelines.md` when the user has an explicit platform/listing/image-set intent (e.g., mentions a platform name, asks for "main images", "listing images", or a "product image set"). For generic single-image cleanup, background change, resize, recolor, or other non-listing edits, do **not** load this reference.

> **Batch Generation is not a standalone scene** — it is a workflow modifier. When a batch is detected, load the Batch Generation section of `references/platform-product-guidelines.md` on top of the matched output-scene references below.

**Reference loading index**:

| Matched Scene | Must Load | When this scene does NOT apply |
|---------------|-----------|--------------------------------|
| Platform Product Image | `references/platform-product-guidelines.md` plus each selected output scene reference | No platform/listing/image-set intent (e.g., generic cleanup, background change, resize, recolor) |
| Batch Generation (workflow modifier) | `references/platform-product-guidelines.md` (Batch Generation section) **plus** each matched output scene reference | User uploads a single image, or gives distinct per-image instructions |
| White Background | `references/white-background.md` | User wants non-white background, scene swap, or native resize/crop only |
| Watermark / Element Removal | `references/remove-watermark.md` | User wants product/package text replacement/removal, translation, or new marketing copy |
| HD Upscale | `references/hd-upscale.md` | User wants file-size increase, format conversion, or content changes |
| Image Resize | `references/image-resize.md` | User wants content changes, cropping, compression, format conversion, file-size-only changes, or platform final delivery resize |
| Scene Image | `references/scene-image.md` | User wants pure white background only, product recolor only, or text-only changes |
| SKU Color Change | `references/sku-color-change.md` | User wants background color change rather than product color change |
| Logo Customization | `references/logo-customization.md` | User asks to design a new logo from scratch |
| Model Showcase | `references/model-showcase.md` | User wants model plus new lifestyle scene or selling-point copy; then load Scene Image or Selling Point too |
| Image Detail | `references/image-detail.md` | User wants callouts, annotations, dimensions, or marketing copy |
| Selling Point Image | `references/selling-point.md` | User only wants a local product detail crop without text/layout |
| Image Translation | `references/image-translation.md` | User asks to replace only specified same-language text |
| Text Editing | `references/text-editing.md` | User asks to translate all visible text or add new marketing copy |
| Logo Design | `references/logo-design.md` | User already has a logo and wants it applied to a product |
| Tech Pack | `references/tech-pack.md` | User wants an e-commerce spec infographic rather than production/OEM drawings |
| Process Flowchart | `references/process-flow.md` | User wants a tech pack/OEM drawing or only text explanation without an image |
| General Style Enrichment | `references/style-guide.md` | A specialized scene already provides enough prompt detail |

---

## Request Processing Pipeline

Process every request through these steps in order:

### Step 0 — Image Intake

If the user provides one or more images, inspect the image before routing.

Identify:
- Main subject
- Elements that must be preserved
- Visible text, logos, labels, watermarks
- Current aspect ratio
- Quality issues such as blur, low resolution, artifacts, occlusion
- Whether the image appears to be a product photo, poster, document, logo, model photo, or scene photo

Use this intake result to build preservation clauses and choose routing.
Never infer hidden or occluded product details.

### Step 0.5 — Image Edit Source Preflight

Before any `image_edit` call, prepare every reference image:

1. **Read before edit (measure exact pixels)**: inspect the image source and confirm it is reachable, decodable, and image-like. You MUST capture the exact pixel width×height (e.g., `2064×1008`) — never infer orientation or ratio from visual appearance. Also capture format/MIME, file size when available, visible content summary, and any quality risks that affect routing. The measured W×H is the authoritative input for the dimension rule in step 5.
2. **Local path handling**: if the user provided a local filesystem path, do not pass it directly to `image_edit`. Read and validate the local file first, upload it to the configured CDN or asset host, then use the returned HTTPS URL as the `reference_images` input.
3. **Remote URL handling**: if the user provided an HTTP(S) URL, verify that it can be read as an image before editing.
4. **Fail closed**: if the image cannot be read, decoded, verified, or converted to an HTTPS URL, stop before `image_edit` and report the blocker. Do not guess-edit an unread image or pass `file://` / raw local paths to remote image-edit tools.
5. **Preserve original dimensions (mandatory)**: keep the edited output at the source image's proportions. How to pass this depends on the mode resolved by **Execution Mode Resolution**:
   - **Tool supports `auto`/`auto_generation`** → if the user did NOT explicitly provide a `size` or `aspect_ratio`, you MUST fall back to passing BOTH the original image's exact measured `size` (the pixel W×H from step 1, e.g., `2064×1008`) AND its closest supported `aspect_ratio` enum value (from the Auto-Match Table). This is deterministic — compute it once from the measured pixels. For any parameter the user did specify, use the user's value.
   - **Tool does NOT support `auto` (falls back to `simple`/`complex`)** → pass ONLY the closest supported `aspect_ratio` enum value from the Auto-Match Table — do NOT pass a raw/arbitrary ratio, and do not rely on `size` in this mode.
   - **Deterministic, single-shot**: the size/ratio parameters are fully determined by the measured source pixels — pass them on the FIRST call. Do NOT run the same edit multiple times hoping for a better ratio. A wrong output ratio/size means the parameter was omitted or wrong, so fix the parameter — do not retry with a different guessed ratio.
   - Never let the tool silently fall back to `1:1` or an arbitrary default when the source has a different shape — this prevents unwanted cropping, stretching, or padding of the product.
   - **Override priority (highest first)**:
     1. **Explicit user request** — a user-specified ratio/size always wins.
     2. **Platform product image set (HARD requirement)** — for a platform image set, the platform's required ratio/size from `references/platform-product-guidelines.md` (e.g., Alibaba.com 1:1, 1000×1000) is MANDATORY. Do NOT apply the source-following rule here; use the platform value regardless of the source image's shape.
     3. **Resolution-changing tasks** — for HD upscale or an explicit target resolution (1K/2K), pass the target `size`/`resolution` instead of the source size; proportions still follow the source unless overridden above.

When uploading local images, avoid exposing sensitive metadata: strip EXIF/GPS data unless the user explicitly needs it preserved, and do not log signed CDN URLs or private local paths.

### Step 1 — Outcome Intent First

Before choosing any `task_type`, identify the user's desired final outcome.

Classify the request by outcome intent first:

| Intent | Meaning |
|--------|---------|
| `platform_main_image` | Platform-ready hero/main product image |
| `platform_image_set` | Multi-image set for a listing |
| `product_cleanup` | Cleaner, more professional product photo |
| `background_replacement` | Change or replace background |
| `text_removal` | Remove specified text, ownership overlays, or visual clutter — route by target type: product/package text → Text Editing (`references/text-editing.md`); watermark/URL/contact/QR/non-product overlay → Watermark Removal (`references/remove-watermark.md`) |
| `text_replacement` | Replace or translate text in image |
| `selling_point_image` | Marketing layout with copy/callouts |
| `logo_design` | Create a new logo from scratch |
| `logo_customization` | Apply existing logo to product/material |
| `upscale_or_restore` | Improve clarity/resolution |
| `native_delivery` | Resize, crop, compress, convert format |

`task_type` is only an execution hint. It must not replace outcome-intent analysis.

### Step 2 — Native vs AI Task Interception

Before invoking any AI image tool, first decide whether the request should be handled by a **native (non-AI) tool** or by the **AI Image Resize scene**. The deciding factor is what the user wants to change:

- **File weight, format, or orientation without content change** → native tool.
- **Pixel dimensions or aspect ratio without content change** → AI Image Resize (`image_edit`).

#### Native (non-AI) operations

If the user's core intent is to change the image's file weight, format, or orientation without altering content, route to native tools.

| Signal | Action |
|--------|--------|
| Crop / trim / cut to dimensions (not SKU asset creation) | Use crop/resize tool |
| Compress / reduce file size / "under X MB" | Use compress tool |
| Format convert (PNG/JPG/WebP) | Use format_convert tool |
| Rotate / flip | Use transform tool |

File-size constraints ("under 2MB") always signal native compression, not HD upscale.

#### AI dimension changes (Image Resize)

If the user asks to **resize to WxH, change dimensions, or change aspect ratio** while keeping the subject/content unchanged, load `references/image-resize.md`, compute the target size, and call `image_edit`.

**Key indicator**: phrases like "keep everything else unchanged" or "just resize" with explicit pixel dimensions or aspect ratio strongly signal the Image Resize scene.

SKU-related requests such as "split into SKU images" or "make each SKU into a listing image" must go through Step 2.5 before deciding native vs AI.

**Mixed requests**:
- Content-changing AI operations usually run first.
- Image Resize usually runs last when combined with content edits.
- Final delivery operations such as crop, compress, and format conversion usually run last.
- Only run crop/resize first when the user explicitly requires a fixed canvas before editing.

> If the Agent toolset lacks a dedicated resize/compress tool, inform the user that this operation is not supported by the AI image tools and suggest alternatives.

### Step 2.5 — SKU Asset Workflow

SKU requests are not always native-only. First classify the user's SKU intent before selecting tools.

| SKU Intent | User Signals | Route |
|------------|--------------|-------|
| **Extract existing SKUs** | "split/crop/separate each SKU", "do not change products", "export each visible style" | Use native crop/segmentation/background removal first. Do not redraw products. |
| **Standardize SKU listing images** | "make each SKU into a listing/main image", "white background SKU images", "Alibaba SKU images", "same style/composition for each SKU" | First isolate each visible SKU, then use `image_edit` per SKU with strict product fidelity. Final resize/format is native. |
| **Generate SKU variants** | "generate colors/styles", "create red/blue/green variants", "make more SKU options" | Use `image_edit` with SKU Color Change when a reference exists; use generation only when the user explicitly asks for new variants. |

Rules:
- Never invent SKU count, colors, materials, or variants unless the user explicitly requested them.
- For batch SKU work, first identify the expected output count. If the count is unclear or visual separation is ambiguous, ask for confirmation.
- For listing-ready SKU images, output one image per SKU. Do not satisfy a multi-SKU request with one combined image.
- Preserve each SKU's exact color, pattern, shape, material, labels, accessories, and visible details.
- Use AI only for listing standardization or requested variant generation; use native tools for pure extraction/crop/format delivery.

### Step 3 — Ambiguity Check

Evaluate whether the request contains enough information to proceed:

| Ambiguity Signal | Action |
|------------------|--------|
| No actionable verb ("edit this", "process these") | Ask what specific changes are needed |
| Safe default subjective requests ("make it cleaner / more professional / optimize / fix this image") | Ask the user what specific changes they want before editing |
| Context-only reply ("yes", "right", "go ahead") with no prior clear instruction | Ask what the user would like done |
| Folder/batch reference without per-image instructions | List files and ask what operation to apply |

| Ambiguity Type | Examples | Action |
|----------------|----------|--------|
| Blocking ambiguity | "edit this", "process these", folder with no operation | Ask user |
| Safe default ambiguity | "make it more professional", "optimize product image", "make it cleaner", "fix this image" | Ask user what specific changes they want |

Do not apply safe visual defaults (cleaner background, balanced composition, softer lighting, etc.) without first confirming the user's intent. Even subjective cleanup requests must be clarified so the agent does not guess the desired outcome.

Do not add new claims, text, logos, certifications, dimensions, materials, or product features unless the user provides them.

Ask the user with specific, selectable options when the host provides a prompt UI; otherwise ask a concise plain-language question (e.g., "What would you like to do: change background, remove watermark, adjust colors, add text, or something else?").

### Step 4 — Multi-Intent Execution Planner

If the request contains **2 or more intents** (including mixed AI + non-AI), plan by output image and minimize AI calls. Decomposition is for reasoning; it does not automatically mean sequential execution.

1. **Identify** all requested intents and the expected output count.
2. **Separate native delivery intents**: resize, crop, compress, rotate, and format conversion should usually run last with native tools and should not trigger an AI call.
3. **Group AI intents by output image**: if several visual changes belong to the same final image, merge them into one prompt when the tool can satisfy them together.
4. **Load references for every matched scene** using the Reference Loading Contract.
5. **Choose execution mode**:
   - `single_purpose`: only when the request exactly matches one dedicated task and no other visual/layout/platform changes are needed.
   - `merged_simple`: one `simple_generation` call for compatible product-fidelity edits such as scene/background, white/light hero styling, centering, shadow, lighting cleanup, local cleanup, logo placement, or product color change.
   - `merged_complex`: one `complex_generation` call for dense layouts, selling-point images, comparison grids, tech packs, or multi-region visual design in a single output.
   - `true_sequential`: only when a previous output is required before the next step, when tool limitations force it, or when the user explicitly requests separate intermediate outputs.
6. **Execute the minimum number of AI calls needed** for the requested output count, then apply native delivery operations last.
7. **Aggregate** results and state any skipped or native-only operations.

**Cost guard**:
- Prefer one AI call per requested output image.
- Do not multiply calls by the number of detected intents when the intents can be expressed in one prompt.
- If a plan would require more AI calls than the requested output count, compress the plan first; ask or explain only when compression would reduce quality or violate safety/tool constraints.
- Platform image sets and SKU batches may require multiple calls because the user expects multiple output images, but each output should still use the fewest feasible calls.

**Merge examples**:

| Request | Preferred Execution |
|---------|---------------------|
| "Make an Amazon main image with white background, centered product, natural shadow" | Load Platform + White Background, then one `simple_generation` call |
| "Remove small clutter, change to white background, and improve lighting" | Load relevant references, then one `simple_generation` call unless clutter is a complex authorized watermark |
| "Change product to red and put it on a gray studio background" | Load SKU Color Change + Scene Image, then one `simple_generation` call |
| "Add my logo and make it a clean listing hero" | Load Logo Customization + Platform/White Background if relevant, then one `simple_generation` call |
| "Create 5 Alibaba listing images" | One planned call per requested output image, not one call per sub-intent inside each image |

Use `true_sequential` for cases such as isolating each SKU before standardization, removing a dense watermark before a high-fidelity edit, or generating separate tech-pack drawings requested as distinct outputs.

### Step 5 — Scene Routing

Match the request against the Scene Router below (Priority 1 → 4). Use the first matching priority level.

**Batch generation check (parallel)**: If the user uploads multiple images and asks for the same operation to be applied to all of them, this is a batch-generation workflow. Route to the appropriate output scene(s) below **and** load `references/platform-product-guidelines.md` (Special Scenario — Batch Generation section) for the batch workflow rules. Do not treat it as a single-image request repeated N times.

---

## Scene Router

### Priority 1 — Platform Product Image (Composite)

| Trigger | Reference | Action |
|---------|-----------|--------|
| User mentions a specific e-commerce platform (Amazon, eBay, Walmart, Shopify, Etsy, AliExpress, TikTok Shop, Shopee, Lazada, Alibaba.com, 1688) AND requests main image / image set / listing images | `references/platform-product-guidelines.md` | Must load platform specs, then load each selected output scene reference. Plan by requested output image and merge compatible edits per Step 4. |

Platform Product Image is a composite workflow — it consults platform requirements first, then delegates to White Background, Scene Image, Model Showcase, etc. as sub-tasks.

**Sub-task execution rules:**
- When the user uploaded product images, ALL sub-tasks MUST use `image_edit`. Never use `image_generate` when reference images exist.
- Sub-tasks that require user-supplied required inputs may trigger clarification before executing:
  - **Selling Point Image** — when selling points must be inferred (see `references/selling-point.md`).
  - **Process Flowchart** — when production stages must be inferred (see `references/process-flow.md`).
  - **Logo Design** — when brand name/context is missing (see `references/logo-design.md`).
- All other sub-tasks execute directly unless safety or missing required inputs block execution.

**Platform image set planner**:
When the user requests multiple listing images (e.g. "3/5/6/11 main images", "main images + detail page images", "Alibaba.com product image set"), create an execution plan before generation:

1. Hero/main image — full product, clean white or light background, no promotional text unless the platform allows it
2. Scene image — B2B or use-context background while preserving the product
3. Detail image — visible material/structure close-up only
4. Selling-point image — only user-provided or visible/verifiable claims
5. Scale/usage image — only if supported by the image or user request
6. Packaging/accessory image — only if visible or user-provided

Rules:
- Generate or edit one final image per planned output. Do not split a single planned output into multiple AI calls unless Step 4 requires `true_sequential`.
- If the tool can only produce one image per call, explicitly run multiple calls or state the limitation.
- Output count must match the requested count when feasible; otherwise explain which planned images were produced and which remain.
- For uploaded product images, every planned image must preserve product identity and use `image_edit`.

### Priority 2 — Single-Operation Scenes

These are exact single-operation requests. Resolve the execution mode via **Execution Mode Resolution** (all are **standard** scenes). Express the operation through the prompt.

| Scene | Trigger Keywords | Reference | Mode class |
|-------|-----------------|-----------|-----------|
| **White Background** | white background, pure white bg, remove background to white | `references/white-background.md` | standard |
| **Watermark / Element Removal** | remove authorized watermark, remove URL/contact/QR overlay, remove accidental overlay, remove non-product object/clutter | `references/remove-watermark.md` | standard |
| **HD Upscale** | upscale, enhance resolution, make clearer, sharpen, higher quality | `references/hd-upscale.md` | standard |
| **Image Resize** | resize to WxH, change dimensions, scale to, aspect ratio W:H, make it Xpx wide/tall, square | `references/image-resize.md` | standard |

**Single-Operation Use Rule**:
Route here only when the user's request exactly matches the single operation and does not require layout, composition, lighting, platform compliance, text editing, or multiple visual changes.

Do not treat every "delete text/logo" request as Watermark Removal. If the target is product/package text, a product brand mark, or user-specified local text, route to Text Editing's local text removal flow. Otherwise, route to Watermark Removal.

**Single-Operation Fallback**:
Switch to a merged plan (or `true_sequential`) when:
- The request contains 2 or more intents
- The user asks for platform-ready or listing-ready output
- The user uses broad outcome language such as "professional", "optimized", "cleaner", "main image", or "selling image"
- The user requests follow-up edits after the initial single operation

### Priority 3 — Specialized Editing Scenes

| Scene | Trigger Keywords | Reference | Mode class |
|-------|-----------------|-----------|-----------|
| **Scene Image** | scene shot, change/swap background, place in environment, lifestyle shot | `references/scene-image.md` | standard |
| **SKU Color Change** | recolor product, change product color, SKU color variant | `references/sku-color-change.md` | standard |
| **Logo Customization** | print logo on product, logo mockup, emboss/engrave/stamp logo | `references/logo-customization.md` | standard |
| **Model Showcase** | model photo, add model, model wearing/holding product | `references/model-showcase.md` | standard |
| **Image Detail** | detail shot, zoom in, close-up of texture/stitching | `references/image-detail.md` | standard |
| **Selling Point Image** | selling point image, highlight features, comparison image, vs competitors | `references/selling-point.md` | standard, or dense-layout for multi-region/comparison |
| **Image Translation** | translate text in image, convert image text to [language] | `references/image-translation.md` | standard |
| **Text Editing** | change text in image, replace text, fix typo, update price/date | `references/text-editing.md` | standard, or dense-layout for multiple/perspective text |
| **Logo Design** | design a logo, create brand mark, logo from scratch | `references/logo-design.md` | dense-layout (generation); standard for editing an existing logo |
| **Tech Pack** | tech pack, dimension drawing, manufacturing spec, assembly diagram | `references/tech-pack.md` | dense-layout |
| **Process Flowchart** | process flow, manufacturing flow, craft flow, how it's made, production process diagram | `references/process-flow.md` | dense-layout |

> **Mode class** maps to a `task_type` through **Execution Mode Resolution**. The exact value depends on the tool declared for the scene: use `auto`/`simple`/`complex` for `image_generate`, and `auto_generation`/`simple_generation`/`complex_generation` for `image_edit`.

> **Image Translation mandatory rule**: When the user requests an edited translated image, load `references/image-translation.md` and call `image_edit` after required inputs are available. Do not substitute a text-only translation for an image-edit request.

### Priority 4 — General (Fallback)

If no specialized scene matches, use semantic instructions and choose the tool based on whether a reference image exists:

- **No reference image** (pure text-to-image generation): use `image_generate` with `simple` or `complex` based on complexity.
- **Reference image provided**: use `image_edit` with `simple_generation` or `complex_generation` based on complexity.

Refer to `references/style-guide.md` for prompt enrichment vocabulary (atmosphere, composition, lighting, materials).

### Key Disambiguation Rules

These cover the most commonly confused routing decisions:

| Ambiguous Request | Correct Route | Why |
|-------------------|---------------|-----|
| "Change background to white" | **White Background** | Pure white (#FFFFFF) → single-purpose task |
| "Change background to kitchen / blue / gray" | **Scene Image** | Any non-pure-white background = scene swap |
| "Recolor the product body" | **SKU Color Change** | Product color, not background |
| "[Platform name] + white background main image" | **Platform** → White Background | Platform keyword → Priority 1 first |
| "Zoom in on zipper detail" vs "Highlight waterproof feature" | **Image Detail** vs **Selling Point** | Pure zoom (no text) → Detail; zoom + marketing copy → Selling Point |
| "Lifestyle selling point image" / "Detail with callouts" / "Model photo with selling points" | **Selling Point Image** | Marketing copy/layout/callouts/comparison + detail/scene/model keywords → Selling Point wins; only it outputs copy + layout + visual anchors together |
| "Add a model, keep the original background" vs "Model in a new lifestyle scene" | **Model Showcase** vs **Scene Image** | Only add model while preserving original background → Model Showcase; model + new background/environment → Scene Image |
| "Make image clearer" vs "Generate a 2K image" | **HD Upscale** vs Resolution Routing | No resolution specified → upscale; explicit resolution → see below |
| "Resize to 1024x768" vs "Make it clearer" | **Image Resize** vs **HD Upscale** | Dimension target specified → Image Resize; quality-only → HD Upscale |
| "Design a logo" vs "Print logo on product" | **Logo Design** vs **Logo Customization** | From scratch → Design; apply existing → Customization |
| "Translate text in image" vs "Change price from 50% to 70%" | **Image Translation** vs **Text Editing** | Cross-language → Translation; same-language replacement → Text Editing |
| "Delete specified product text" vs "Remove authorized watermark/overlay" vs "Add marketing copy" | **Text Editing** vs **Watermark Removal** vs **Selling Point** | Product/package text removal → Text Editing local removal; authorized watermark/contact/QR/non-product overlay removal → `watermark_removal`; add new text + layout → Selling Point |
| "Tech pack / dimension drawing" vs "Mark dimensions as selling point" | **Tech Pack** vs **Selling Point ③** | Production/OEM specs → Tech Pack; marketing display → Selling Point |
| "Manufacturing process flowchart / how it's made" vs "Tech pack" vs "Selling point infographic" | **Process Flowchart** vs **Tech Pack** vs **Selling Point** | Consumer-friendly numbered production stages with photos → Process Flowchart; OEM specs/drawings → Tech Pack; marketing feature highlights → Selling Point |

### Strict Product Fidelity Mode

Enable this mode whenever the user asks to keep the product unchanged, produce platform/listing images from a reference, remove/replace text while preserving the product, make a white-background hero, or change only the surrounding scene/background.

Append this constraint to all relevant `image_edit` prompts:

```
Keep the product exactly unchanged — preserve geometry, color, texture, labels, and camera angle.
Only change: [describe allowed changes here].
Do not redraw, simplify, or invent any product detail.
```

Keep the constraint concise (≤3 lines). Verbose constraint lists (enumerating 20+ protected items) do not improve model compliance and can reduce prompt execution quality.

When product fidelity conflicts with a more creative instruction, product fidelity wins unless the user explicitly asks to redesign the product.

**Task type rule**: When Strict Product Fidelity Mode is active, prefer `simple_generation` over `complex_generation` unless the loaded reference requires dense layout or annotations. In many runtimes, complex edit modes are more likely to affect the whole image, increasing product drift risk.

### Resolution Routing

When the user specifies an output resolution, translate the shorthand into concrete pixel targets and pass them explicitly. Do **not** rely on tool defaults, which often fall back to 1024×1024.

> **Current skill scope**: 1K and 2K resolution targets are supported. 4K is **not** supported by this skill; if the user asks for 4K, inform them that only up to 2K is supported and offer 2K instead.

#### Common resolution shorthand

| Shorthand | Long-edge pixel target | Typical `size` / `resolution` value |
|-----------|------------------------|-------------------------------------|
| 1K | 1024 px | `1024x1024` (or match source aspect ratio, e.g. `1024x768`) |
| 2K | 2048 px | `2048x2048` (or match source aspect ratio, e.g. `2048x1536`) |

> For non-square images, keep the **long edge** at the target pixel value and compute the short edge from the source aspect ratio. Both dimensions must be rounded to multiples of 16. Do not stretch or crop the image to a square unless the user or platform explicitly requires it.

#### Parameter rules by execution mode

| Resolved mode | Supported parameters | How to express 2K |
|---------------|----------------------|-------------------|
| `auto` / `auto_generation` | `size` (concrete pixels) | Pass `size` with long edge = 2048 px and short edge computed from the source ratio, both rounded to the nearest multiple of 16. Example: source `1920x1080` → `size: "2048x1152"`. |
| `simple` / `simple_generation` | `aspect_ratio` + `resolution` | Pass the closest supported `aspect_ratio` from the Auto-Match Table **and** `resolution: "2K"`. Do **not** pass raw pixel `size` in this mode. |
| `complex` / `complex_generation` | `aspect_ratio` + `resolution` | Same as simple mode: closest `aspect_ratio` + `resolution: "2K"`. HD upscale is a standard scene and should prefer `auto`/`simple` when possible. |

> **16-multiple rule**: when computing `size` for `auto`/`auto_generation`, the width and height must both be divisible by 16. Round the computed short edge to the nearest multiple of 16 (e.g. `1365` → `1360` or `1376`).

#### Routing by request type

| Request | Action |
|---------|--------|
| "Make it clearer / sharpen" (no resolution specified) | Use `hd_upscale` with source proportions. |
| "Generate at 1K / 2K" or explicit resolution from scratch | Use `image_generate` with the target `size`/`resolution` per the mode rules above. |
| Upscale an existing image to 1K / 2K | Use `image_edit` under the resolved standard mode and pass parameters per the mode rules above (see `references/hd-upscale.md`). Keep source proportions. |
| "4K" or higher | **Not supported**. Inform the user and offer 2K as the maximum supported resolution. |

> **Why results become 1024×1024**: most image tools default to 1024×1024 when no explicit `size` or `resolution` is provided. Always pass the concrete target pixel dimensions or the `resolution` enum for resolution-changing requests.

#### Pre-invocation parameter check

Before calling the image tool for a 1K/2K request, verify the parameters match the mode resolved by **Execution Mode Resolution**:

| Resolved mode | Required parameter shape | Forbidden parameter shape |
|---------------|--------------------------|---------------------------|
| `auto` / `auto_generation` | `size: "<width>x<height>"` where both dimensions are multiples of 16 and the long edge matches the target resolution | Do **not** pass `resolution` + `aspect_ratio` as a substitute for `size` |
| `simple` / `simple_generation` | `aspect_ratio` (closest supported) + `resolution: "1K"` or `"2K"` | Do **not** pass raw pixel `size` |
| `complex` / `complex_generation` | `aspect_ratio` (closest supported) + `resolution: "1K"` or `"2K"` | Do **not** pass raw pixel `size`; avoid for HD upscale unless forced by tool limitations |

> If the constructed parameters do not match the resolved mode, revisit Step A of **Execution Mode Resolution** and correct the parameter set before invoking the tool.

### Task Type Safety Rules

`task_type` is an execution hint, not the user's intent.

Before selecting a task_type:
1. Identify the user's desired final outcome.
2. List all required visual changes.
3. Check whether a single-purpose task_type can satisfy all required changes.
4. If not, use a merged `simple_generation`, merged `complex_generation`, or `true_sequential` plan only when required by Step 4.

Use single-purpose task_types only when the user requests exactly one operation:
- `white_background`: only pure white background replacement
- `hd_upscale`: only clarity/resolution improvement
- `watermark_removal`: authorized removal of watermarks, URLs, contact info, QR codes, accidental overlays, stains, dust, scanner marks, or non-product visual clutter. Product/package text and product brand marks are not this route.

Do not use single-purpose task_types for:
- Platform-ready product images
- General image optimization
- "Make it professional"
- Mixed requests
- Follow-up refinements
- Layout, text, selling-point, or composition changes
- Requests involving product coverage, shadows, centering, lighting, or platform compliance

### Task Type Misrouting Guard

| User Request | Avoid | Prefer |
|-------------|-------|--------|
| "Make this an Amazon main image" | `white_background` only | Platform workflow + prompted edit |
| "Make it cleaner/professional" | `hd_upscale` only | Product cleanup with safe defaults |
| "Remove text and make white background" | `watermark_removal` only | Load Text Editing or Watermark Removal + White Background; prefer one merged `simple_generation` unless true sequential is required |
| "Optimize this product photo" | Single-purpose task_type | Product cleanup / platform intent |
| "Change background and improve lighting" | `white_background` | Load Scene Image; use one merged `simple_generation` |
| "Turn this into a listing image" | Any single-purpose task_type | Platform or product image workflow |

### Execution Mode Resolution (Tool Mode Contract)

This is the **single source of truth** for choosing the execution mode / `task_type`. All reference files defer to this section — a reference only declares whether its scene is **standard** or **dense-layout**, and whether it generates from scratch (`image_generate`) or edits an existing image (`image_edit`). It never hard-codes a task_type.

**Step A — Detect tool capability**: before selecting any `task_type`, inspect the actual tool schema / definition available in the current runtime. Do **not** assume a mode is supported and do **not** default to `simple`/`simple_generation` without this check.

- For `image_generate`, examine the valid `task_type` enum values. If `auto` is listed, the tool supports auto mode.
- For `image_edit`, examine the valid `task_type` enum values. If `auto_generation` is listed, the tool supports auto mode.
- If the schema does not expose `auto`/`auto_generation`, auto mode is unavailable for this runtime.
- If you cannot inspect the schema, attempt an `auto`/`auto_generation` call only when the host runtime explicitly advertises it; otherwise treat it as unavailable.

**Step B — Resolve the mode**:

| Tool supports `auto`? | Standard scene | Dense-layout scene |
|-----------------------|----------------|--------------------|
| **`image_generate` supports `auto`** | `auto` | `auto` |
| **`image_edit` supports `auto_generation`** | `auto_generation` | `auto_generation` |
| **`image_generate` does NOT support `auto`** | `simple` | `complex` |
| **`image_edit` does NOT support `auto_generation`** | `simple_generation` | `complex_generation` |

- When `auto`/`auto_generation` is available, **always prefer it** — it lets the tool decide simple vs complex internally, so do NOT hand-pick simple/complex.
- When `auto` is unavailable, fall back to `simple`/`simple_generation` for standard scenes, and `complex`/`complex_generation` only for dense-layout scenes.

> **Anti-default guard**: Never choose `simple`/`simple_generation` just because it is "safer" or because you did not check the schema. The parameter shape differs between modes (e.g., `size` for `auto_generation` vs. `aspect_ratio` + `resolution` for `simple_generation`). Passing the wrong shape silently degrades output quality or resolution. Always confirm the resolved mode from Step A before constructing the parameter set.

**Dense-layout scenes** (need `complex` fallback when `auto` is unavailable):
- Selling-point images with multi-region layout / comparison grids
- Tech pack drawings
- Logo design generation from scratch
- Multi-region text editing or difficult perspective/curved text
- Full creative posters / infographics

**Standard scenes** (fall back to `simple`/`simple_generation`): white background, HD upscale, watermark/element removal, scene/background swap, SKU recolor, model showcase, image detail, logo customization, image translation, single-region text editing, and any product-fidelity edit.

**White background, HD upscale, and watermark/element removal** are standard scenes: express their intent through the prompt (e.g., "replace background with pure white #FFFFFF", "enhance resolution and sharpness without changing any content", "remove the authorized watermark/overlay only") and run them under the resolved mode above. They follow the general Aspect Ratio rules (match the source ratio).

**Strict Product Fidelity always applies**: whenever a product reference is present and must stay unchanged, keep Strict Product Fidelity Mode active regardless of the resolved mode.

### Simple vs Complex task_type

| Criteria | `simple` / `simple_generation` | `complex` / `complex_generation` |
|----------|-------------------------------|----------------------------------|
| Edit scope | Single region, localized | Multi-region or full-image overhaul |
| Product preserved? | Yes — this is the safe choice | Higher redraw risk |
| Elements modified | ≤2 | ≥3 or major composition change |
| Typical use | Background swap, scene change, single recolor, logo stamp, product-on-white | Poster with selling points, comparison grid, infographic, tech pack |

**When in doubt, use `simple_generation`**. The downside of `simple_generation` on a complex task is lower layout quality; the downside of `complex_generation` on a fidelity task is product destruction.

### `complex_generation` Risk Guard

`complex_generation` is higher-risk for product-fidelity tasks because many image-edit runtimes treat complex edits as broader redraws rather than localized edits. Use it only when the requested output genuinely needs dense layout, annotations, comparison grids, or full-image design composition.

**Default to `simple_generation`** unless the task genuinely requires complex layout:

| Scenario | Correct task_type |
|----------|------------------|
| Background swap / scene change (product preserved) | `simple_generation` |
| Single-region edit (recolor, remove object, add logo) | `simple_generation` |
| White/light hero image from product photo | `simple_generation` |
| Platform main image (product preserved) | `simple_generation` |
| Model showcase with product | `simple_generation` |
| Localized product/package text removal | `simple_generation` via Text Editing local removal |
| Authorized watermark/contact/QR/non-product overlay removal | `watermark_removal` |
| Dense annotations / selling-point layout / comparison grid | `complex_generation` |
| Multi-region independent edits in one image | `complex_generation` |
| Full creative poster / heavy compositional redesign | `complex_generation` |
| Tech pack with dimension callouts | `complex_generation` |

**Rule of thumb**: If the product must stay unchanged, use `simple_generation`. The "complex" in the user's request description does not mean you should use `complex_generation` — a visually rich background or detailed scene is still a `simple_generation` task when the product is preserved.

If using `complex_generation` with a product reference, always enable Strict Product Fidelity Mode.

---

## Prompt Construction

### Generation Prompt Formula

```
[Shot type] of [Subject] in [Setting], [Action/State].
[Style], [Composition], [Lighting], [Color palette], [Quality].
```

| Element | Description | Examples |
|---------|-------------|----------|
| Subject | What to depict (be specific) | "ginger tabby cat", "ergonomic wireless headphones" |
| Setting | Environment/location | "windowsill with afternoon sunlight", "minimalist studio" |
| Style | Overall aesthetic | "cinematic", "watercolor", "flat vector" |
| Composition | Camera/framing | "close-up", "wide-angle", "rule-of-thirds" |
| Lighting | Light source and mood | "golden hour", "soft diffused", "Rembrandt lighting" |
| Color | Palette direction | "Morandi palette", "high saturation", "monochromatic" |
| Quality | Detail level | "8K", "hyperrealistic", "sharp detail" |

For text in images, use explicit quotes: `Display "LIMITED EDITION" in bold serif font`.

### Editing Prompt Formula

```
[Edit instruction targeting specific area].
[Preservation clause (concise, ≤3 lines)].
```

**Preservation clause template** (append to every edit prompt):

```
Keep the product exactly unchanged — preserve geometry, color, texture, labels, and camera angle.
Only change: [specific allowed changes].
Do not redraw, simplify, or invent any product detail.
```

Keep it concise. Long enumeration lists (20+ protected items) do not improve compliance and may reduce model execution quality. The model responds better to clear, short constraints than to exhaustive lists.

**Example — Scene change**:
```
Place the product on a modern kitchen countertop with warm morning light.
Keep the product exactly unchanged — preserve geometry, color, texture, labels, and camera angle.
Only change: background/scene.
Do not redraw, simplify, or invent any product detail.
```

---

## Result Check

After receiving a generation or editing result:

1. **Preservation audit**: Compare the result against the preservation clause. If the model altered protected elements (product shape distorted, text removed, colors shifted), retry with a stronger constraint — e.g., add "CRITICAL:" prefix, list each protected element individually, or reduce edit scope.
2. **Intent completeness**: Verify all user-requested changes are present. If a sub-task from Step 4 was missed, execute the remaining sub-tasks on the current output.
3. **Quality gate**: If the result is clearly unusable (heavy artifacts, wrong subject, garbled text), inform the user and offer to retry with adjusted parameters (e.g., switch `simple_generation` → `complex_generation`, or simplify the prompt).

Hard failure conditions:
- Product shape, structure, SKU color/pattern, packaging text, logo, handle, seam, hole, accessory, or camera angle changed when not requested
- A crop/split/resize/format task was handled by generating a new product image
- Multi-image or multi-SKU output count does not match the requested/confirmed count
- White-background output changes the product or leaves damaged/blurred edges
- Text editing/removal causes garbled text, misspellings, or modifies unspecified text
- Platform hero image includes prohibited overlays, watermarks, contact information, or unsupported claims
- User requested "do not change the product" but the result redraws or reimagines the product

### Scene Acceptance Criteria

| Scene | Acceptance Criteria |
|-------|---------------------|
| White Background | Background is pure white or near #FFFFFF; product shape unchanged; edges clean |
| HD Upscale | More detail without changing identity, color, text, or layout |
| Watermark / Element Removal | Removed target element only; no damage to product or surrounding content |
| Text Editing | Exact requested text; readable; no garbled characters |
| Product Cleanup | Product identity preserved; lighting/background improved; no invented claims |
| Selling Point Image | No fabricated claims; labels readable; text does not cover product |
| Platform Hero | Meets platform background, text, watermark, ratio, and product coverage rules |
| Logo Design | Original, legible, commercially safe, not similar to known trademarks |

> Do NOT silently retry indefinitely. After 2 failed attempts at the same task, inform the user of the limitation and suggest alternatives.

---

## Tool Contract / Host Mapping

Concrete tool names may vary by runtime. Map workflow intent to available tools.

Required conceptual operations:
- Text-to-image generation
- Image-to-image editing
- Native resize/crop/compress/format conversion, if available
- User clarification / selection prompt, if available

For image generation:
- Required: prompt
- Optional: aspect_ratio, resolution, task_type

For image editing:
- Required: reference_images, prompt or task_type
- Optional: aspect_ratio, resolution, size

> When editing, preserve source proportions per Step 0.5: in `auto_generation` mode, if the user did not specify them, fall back to passing BOTH the source `size` and the closest source `aspect_ratio`; in `simple`/`complex` mode pass ONLY the closest source `aspect_ratio`. `aspect_ratio` controls proportions only; `size`/`resolution` control output pixels. Override priority: explicit user ratio/size → platform image-set requirement (HARD — do not follow source) → resolution-changing tasks (HD upscale / target 1K/2K use target size).

If the runtime lacks a native resize/compress/format tool, use available local image-processing libraries when permitted. If unavailable, explain the limitation to the user.

## Tool Reference

### Canonical task_type values

> Selection is governed by **Execution Mode Resolution** above: prefer `auto`/`auto_generation` when the tool supports it; otherwise use `simple`/`simple_generation` (standard) or `complex`/`complex_generation` (dense-layout).

For `image_generate`:
- `auto` (preferred when supported)
- `simple`
- `complex`

For `image_edit`:
- `auto_generation` (preferred when supported)
- `simple_generation`
- `complex_generation`

> White background, HD upscale, and watermark/element removal are expressed via the prompt and run under the resolved mode (see Execution Mode Resolution).

### image_generate

For generating images from scratch (no reference image).

| task_type | When to Use |
|-----------|-------------|
| `auto` | Preferred when supported — the tool auto-selects simple vs complex |
| `simple` | Fallback for single subject, simple scene, basic composition |
| `complex` | Fallback for dense-layout: multi-element poster, 3D render, multi-image set |

### image_edit

For editing an existing image.

| task_type | When to Use |
|-----------|-------------|
| `auto_generation` | Preferred when supported — the tool auto-selects simple vs complex |
| `simple_generation` | Fallback for standard edits: background swap, scene change, white background, HD upscale, watermark/element removal, single-region changes. **Preferred when product must stay unchanged.** |
| `complex_generation` | Fallback for dense-layout only: multi-region layout, poster, infographic, comparison grid, tech pack. **Do NOT use for product-fidelity edits.** |

---

## Aspect Ratio

### Supported Values

```
1:1 | 2:3 | 3:2 | 3:4 | 4:3 | 4:5 | 5:4 | 9:16 | 16:9 | 21:9
```

### Selection Rules

Priority order: explicit user ratio → platform requirement (hard) → source-following → default. Source-following depends on tool mode: in `auto_generation`, if the user didn't specify, fall back to source `size` + closest `aspect_ratio`; in `simple`/`complex` pass ONLY the closest source `aspect_ratio` (see Step 0.5).

1. **User specified a supported ratio** → use it directly
2. **User specified an unsupported ratio** (e.g., 5:3) → inform user and ask to choose from supported list
3. **Platform product image set** (Priority 1 workflow) — **HARD requirement**: the platform's required ratio/size from `references/platform-product-guidelines.md` (e.g., Alibaba.com 1:1, 1000×1000) is MANDATORY and does NOT follow the general source-following rule. Only an explicit user ratio can override it.
4. **User did not specify (non-platform)** →
   - Scenes with an uploaded image → follow the source per Step 0.5 (auto → source `size` + closest `aspect_ratio`; non-auto → ONLY the closest `aspect_ratio` from the table below)
   - Scenes without an image → default `1:1`

### Auto-Match Table (for uploaded images without user-specified ratio)

| Image Shape | W:H Range | Best Match |
|-------------|-----------|------------|
| Square | 0.9 – 1.1 | `1:1` |
| Slightly tall | 0.75 – 0.9 | `4:5` |
| Portrait | 0.6 – 0.75 | `3:4` |
| Tall portrait | 0.5 – 0.6 | `2:3` |
| Very tall | < 0.5 | `9:16` |
| Slightly wide | 1.1 – 1.35 | `5:4` |
| Landscape | 1.35 – 1.6 | `4:3` |
| Wide landscape | 1.6 – 1.8 | `3:2` |
| Widescreen | 1.8 – 2.2 | `16:9` |
| Ultra-wide | > 2.2 | `21:9` |

> Compute W/H ratio of the uploaded image and find the matching range. If the closest ratio deviates >10% from the original, briefly inform the user about potential cropping.

---

## Multilingual Handling

When the user's input is not in English:

1. **Detect the input language**
2. **Preserve key terms**: Embed the user's original nouns/descriptions directly into the prompt rather than translating them (translation can distort meaning)
3. **Build prompts in English**: The image model works best with English prompts, but anchor key concepts from the original language
4. **Respond in the user's language**

> Example: User writes "A máquina parada (dor financeira)" (Portuguese)
> → Extract: "máquina parada" = stopped machine, "dor financeira" = financial pain
> → Prompt uses: "idle/stopped CNC machine, conveying financial loss and downtime frustration"
> → NOT: "professional, precise, clean machine" (semantic reversal)

---

## Batch Operations

When the user references a folder or multiple images:

1. List the images in the folder/selection
2. If the user has not specified what to do per image, ask for the operation before editing
3. If all images need the same operation → batch-execute the same scene
4. If different images need different operations → classify and route individually
5. **Never guess-edit** folder contents without explicit instructions
