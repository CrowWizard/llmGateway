# HD Upscale

## Routing Header

- **Load when**: user asks to upscale, sharpen, enhance clarity, restore resolution, or make an existing image clearer without content changes.
- **Do not load when**: user asks only to increase file size, convert format, resize dimensions (load `references/image-resize.md`), crop, or change visual content.
- **Merge notes**: do not treat upscale as a separate paid AI step when the user mainly wants native delivery sizing. If content edits are needed, perform content edits first and upscale only when quality is still insufficient or the user explicitly requested it.
- **Hard stop**: this scene must not remove watermarks, change backgrounds, fix layout, edit text, or alter any image content.

## Scene Description

Enhance the resolution and sharpness of an existing image without modifying its content, composition, colors, or elements.

> **Hard constraint**: This scene only enhances clarity. It does not remove watermarks, change backgrounds, adjust elements, or alter image content in any way.

## Apply Method: Concatenate a short enhancement prompt

Pass the image with a short enhancement instruction (resolution/sharpness only), e.g., "enhance resolution and sharpness without changing any content". Do not add any content-changing description.

## Tool Invocation

- Tool: `image_edit`
- Mode: **standard** scene — express the enhancement in the prompt (e.g., "enhance resolution and sharpness without changing any content, colors, text, or layout") and resolve the `task_type` via SKILL.md **Execution Mode Resolution** (prefer `auto_generation`; otherwise `simple_generation`).
- Dimensions (mandatory): this is a resolution-changing task — follow SKILL.md Step 0.5: keep the source proportions and pass the target `size`/`resolution` (do not shrink below the source).

## Content-Type Pre-Check

Before running the enhancement, the Agent should assess the image content type:

| Content Type | Recommendation |
|-------------|----------------|
| Product photos, natural scenes, portraits | Proceed normally with the enhancement |
| **Text-heavy images** (posters, UI screenshots, infographics, documents) | **Warn the user AND continue**: inform the user that "AI HD upscale may cause text distortion or blurring on text-heavy images. Please verify key text and data after processing.", then immediately invoke `image_edit` to run the enhancement. Do **not** pause or stop after the warning. |
| **File size requests** ("I need it to be 2MB", "output is too small") | **This is NOT an AI task**. Route to resize tool or quality-parameter adjustment. Increasing file size ≠ increasing visual clarity. |

> **Warning is informational only**: the text-heavy warning is a risk disclosure, not a blocker. Unless the user explicitly cancels, the Agent must proceed with the HD upscale tool call right after delivering the warning.

## Resolution Routing

When the user specifies an explicit output resolution (1K/2K) rather than "make it clearer", translate the shorthand into concrete pixel targets and pass them explicitly. Do **not** rely on tool defaults, which often fall back to 1024×1024.

> **Current skill scope**: 1K and 2K resolution targets are supported. 4K is **not** supported by this skill; if the user asks for 4K, inform them that only up to 2K is supported and offer 2K instead.

### Resolution shorthand → pixel target

| Shorthand | Long-edge target | Example `size` / `resolution` for source images |
|-----------|------------------|------------------------------------------------|
| 1K | 1024 px | `1024x1024` for square source, or long edge = 1024 keeping source ratio |
| 2K | 2048 px | `2048x2048` for square source, or long edge = 2048 keeping source ratio |

> Always preserve the source aspect ratio. For non-square images, set the long edge to the target pixel value, compute the short edge from the source ratio, and round both dimensions to the nearest multiple of 16. Do not stretch or pad to square unless explicitly required.

### Parameter rules by execution mode

Resolve the `task_type` via SKILL.md **Execution Mode Resolution**. HD upscale is a **standard scene**, so prefer `auto_generation` when the tool schema supports it; only fall back to `simple_generation` when `auto_generation` is confirmed unavailable. Do **not** default to `simple_generation` without checking the tool schema.

| Resolved mode | Parameters to pass | 2K example for source `1920x1080` |
|---------------|-------------------|-----------------------------------|
| `auto_generation` | `size` (concrete pixels) | `size: "2048x1152"` (long edge 2048, short edge = 2048 × 1080/1920 = 1152, both multiples of 16) |
| `simple_generation` | `aspect_ratio` + `resolution` | `aspect_ratio: "16:9"`, `resolution: "2K"` (do **not** pass raw pixel `size`) |
| `complex_generation` | `aspect_ratio` + `resolution` | Same as simple mode. Avoid for HD upscale unless the tool forces it. |

> **16-multiple rule**: when computing `size` for `auto_generation`, width and height must both be divisible by 16. If the computed short edge is not a multiple of 16, round to the nearest multiple (e.g. `1365` → `1360` or `1376`).

### Tool-capability routing

```
Step 1: Inspect the tool schema and confirm whether `auto_generation` is a supported task_type.
Step 2: Resolve mode via SKILL.md Execution Mode Resolution.
Step 3: Construct parameters that match the resolved mode:
    IF resolved mode is `auto_generation`:
        → pass the concrete target `size` (long edge 2048, short edge computed and rounded to 16)
    ELIF resolved mode is `simple_generation` or `complex_generation`:
        → pass the closest supported `aspect_ratio` from the Auto-Match Table AND `resolution: "2K"`
    ELSE:
        → run the enhancement (standard mode) to increase clarity and inform the user that the output size is limited by the tool
Step 4: Before invoking, verify the parameter shape matches the resolved mode. Do not substitute `aspect_ratio` + `resolution` when `auto_generation` was resolved.
```

| User Request | Resolved mode supports target size | Fallback |
|-------------|------------------------------------|----------|
| "Upscale to 1K/2K" | `auto_generation`: pass `size`; `simple`/`complex`: pass `aspect_ratio` + `resolution: "2K"` | enhancement (standard mode); inform user of size limitation |
| "Make it clearer / sharpen" | enhancement (standard mode) | enhancement (standard mode) |

**Key distinction**:
- "Make clearer / sharpen / upscale" = enhance existing image → run the enhancement (standard mode)
- "Upscale to XK" = specified output resolution → pass concrete parameters per mode rules above; never assume simple mode without schema confirmation

## Notes

- **Dimensions**: keep the original proportions and content; this is a resolution-changing task, so pass the target `size`/`resolution` per the mode rules above (see SKILL.md Step 0.5).
- **Single-operation constraint**: if the request includes 2 or more intents, platform/listing readiness, composition changes, lighting changes, or broad optimization language, do not treat it as a standalone HD Upscale task. Route to SKILL.md Step 4 (Multi-Intent Execution Planner) first, and only apply HD Upscale as a final quality step if the user explicitly requested it or if the output still lacks clarity after the primary edits.
- **No content modification**: this scene must not change composition, colors, elements, or background — only resolution and sharpness
