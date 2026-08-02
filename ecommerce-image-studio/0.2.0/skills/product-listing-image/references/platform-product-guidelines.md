# E-Commerce Platform Product Image Guidelines
> **Updated**: 2026-05-11

## Routing Header

- **Load when**: user names a covered e-commerce platform and asks for main images, hero images, listing images, product image sets, or listing-risk/compliance-oriented image guidance.
- **Do not load when**: user asks for generic cleanup, background change, logo application, or resize with no platform/listing intent.
- **Merge notes**: use this file to select platform constraints, then load only the output scene references needed for each requested image. Do not split one platform hero into separate AI calls for background, centering, shadow, and coverage when one product-fidelity prompt can satisfy them.
- **Hard stop**: if the user requests latest compliance, official verification, or policy-sensitive review, verify against official platform documentation before finalizing.

## Usage

1. **Primary reference**: Use this document as the default built-in reference for platform product hero/set image requirements.
2. **Official verification**: If the user asks for latest compliance, official verification, listing-risk review, policy-sensitive checks, or requirements not covered here, consult official platform documentation before finalizing.
3. **Platforms covered**: Amazon, eBay, Walmart, Shopify, Etsy, AliExpress, TikTok Shop, Shopee, Lazada, Alibaba.com, 1688.

---

## Platform Image Set Style Systems

> These are **proven principles**, not layouts to photocopy. Match the product's emotional register (sporty / cozy / premium / handmade) first, then regenerate the execution within each platform's grammar.

When a user requests a multi-image listing set for a platform, keep the following cross-platform rules in mind:

1. **One platform = one style system.** All images in the same set share the same background world, lighting language, and palette. Mixed-style sets read as disconnected and usually get rejected.
2. **The product never changes.** Same geometry, weave/texture, and color family across every image. If the product line has messy colors, propose ONE tonal family and get user confirmation before generating the set.
3. **Platform grammar is fixed; execution is category-flexible.** For example, TikTok always requires strong human presence, but the exact mood can shift between "flash-lit editorial sports" and "warm golden cozy" depending on the category.
4. **Style tokens should be repeated verbatim** across shots in the same set to hold background/lighting constant. Number shots ("shot N of 4") to help the model keep continuity.
5. **Before delivering, run a consistency gate:** check that no single shot leaks a different season, venue, or temperature into the set; verify in-image text spelling; flag any inferred multi-view angles or spec numbers that need user sanity-check.

The sections below pair each platform's **hard specs** with an **optional image-set style notes** subsection. Use the specs for compliance; use the style notes as a starting point that should be adapted to the product's emotional register.

---

## Special Scenario — Batch Generation

> Use this scenario when the user uploads **multiple images** and asks for the **same operation** to be applied to all of them (e.g., "batch process these 20 product photos to white background", "make all these images into Amazon main images", "apply the same scene style to every SKU photo").

### Core Rules

1. **User-specified parameters are the hard batch contract.**
   - Any parameter the user explicitly specifies (operation, background color, ratio, lighting mood, output format, platform target, resolution) must be honored for every image.
   - Do not reinterpret the operation differently per image.
   - If the user's request contains ambiguity (e.g., "make them look better"), propose a single safe default and apply it uniformly across the whole batch — do not let the interpretation drift from image to image.

2. **Style consistency is pursued, not enforced rigidly.**
   - The agent should plan prompts so that the batch reads as visually aligned where semantically reasonable, but it is **not required** to force every image into an identical background world, lighting language, or color palette.
   - Use a **batch style token** as a default anchor, then adapt it per image when strict reuse would create semantic or environmental conflicts.
   - Examples of conflicts where strict reuse should be relaxed:
     - A kitchen product and an outdoor product both asked for "warm home scene" — the outdoor product should get an appropriate warm outdoor context, not a kitchen counter.
     - A dark tech gadget and a pastel baby product both asked for "soft shadows" — the lighting quality can stay similar while the surrounding world differs.
   - When in doubt, prefer **coherence in quality and treatment** over **sameness of scene**.

### Workflow

1. **Intake all images first.**
   - List the batch: file names, dimensions, formats, visible content, and any obvious outliers (damaged, wrong category, extreme aspect ratio).
   - Note domain/style diversity: are these all similar products, or mixed categories? This determines how rigid the style lock should be.
   - Flag images that cannot be processed as requested (e.g., a logo-design request on a corrupted file) and report them before starting the batch.

2. **Lock the hard contract; plan a flexible style anchor.**
   - Summarize back to the user: operation, target ratio, output count, and any assumptions.
   - Propose a **flexible style anchor** — a short phrase that captures the shared treatment (e.g., "clean studio lighting, soft shadow, neutral background") without prescribing a single literal scene.
   - Example: *"Batch plan: convert all 12 images to clean white-bg product hero, 1:1, 1600×1600, with consistent studio lighting and soft shadow. If a product clearly belongs in a different environment, we'll adapt the background while keeping the lighting quality uniform."*
   - Ask for confirmation if the request is ambiguous or if outliers exist.

3. **Generate one image at a time with a shared template and per-image adaptation.**
   - Use a reusable prompt skeleton: `[Operation] + [Flexible style anchor] + [Per-image adaptation] + [Per-image preservation clause]`.
   - The **style anchor** should stay as consistent as possible across the batch; the **per-image adaptation** resolves semantic conflicts (e.g., different appropriate environments for different product types).
   - Number outputs ("image N of M") to help the model maintain continuity.

4. **Coherence gate before delivery.**
   - Spot-check at least 3 outputs across the batch: first, middle, and last.
   - Verify that the shared treatment feels consistent at a glance: lighting quality, shadow softness, color temperature, output ratio, and overall finish.
   - Do not flag a result as inconsistent just because the literal background scene differs — flag it only if the **treatment quality** visibly diverges (e.g., one image is harsh flash while others are soft window light).
   - If one output diverges in treatment, identify which style anchor was lost and regenerate only that image with the corrected prompt.

### Difference from Platform Image Sets

| Dimension | Platform Image Set | Batch Generation |
|-----------|-------------------|------------------|
| Input | Usually one reference product | Multiple distinct images |
| Goal | Per-platform campaign coherence | Same operation applied with shared treatment |
| Style unit | One style system per platform | One flexible style anchor per batch, with per-image adaptation |
| Output relationship | 4–6 images tell one product story | Each image is independent but visually aligned where reasonable |
| Ratio | Platform-mandated | User/platform-mandated; may follow source if user asks |
| Background world | Must be shared | Should be coherent in quality, may differ by product domain |

### Batch of Platform Image Sets

When the user uploads **multiple product images** and asks for a **platform image set for each** (e.g., "make each of these 10 products into an Alibaba 6-image set"), the two modifiers overlap. Apply these rules:

1. **One platform set per input image** — treat each uploaded product image as the single reference for its own set. Do not mix products across sets.
2. **Per-set style contract** — within one set, use the platform's style system (shared background world, lighting, palette). Across different sets, the style contract restarts for each new product.
3. **Output mapping** — aggregate results by input image so the user can clearly map each generated image back to its source product (e.g., "Product A — hero", "Product A — scene", "Product B — hero", ...).
4. **Reference count** — load `references/platform-product-guidelines.md` once, then apply its rules independently to each product's set.
5. **Do not average products** — each set preserves only its own product's identity; never generate a "generic" set that blends features from multiple inputs.

### Common Pitfalls

- **Over-locking the scene**: forcing a kitchen counter background onto every image when the batch contains outdoor gear will produce nonsensical results. Lock the treatment, not the literal environment.
- **Drifting treatment adjectives**: replacing "soft natural shadow" with "gentle shadow" or "subtle drop shadow" across calls will break batch coherence. Pick one phrase for the shared anchor and stick to it.
- **Source-ratio following by default**: when the user says "batch process these", they usually expect visual consistency more than pixel-perfect source preservation. Confirm the target ratio instead of silently mixing ratios.
- **Outlier contamination**: one corrupted, miscategorized, or heavily watermarked image can make the whole batch look uneven if its output is included without flagging.
- **Silent enhancement variance**: applying HD upscale to only the low-res images while leaving others untouched creates a visibly uneven batch.

---

## Cross-Border B2C

### 1. Amazon

1. **Hero image count**: 1 hero required; recommended total ≥6 supporting images + 1 video.
2. **White background**: ✅ Mandatory pure white (RGB 255, 255, 255).
3. **Recommended size**: Longest side min 500 px, max 10,000 px; recommended ≥1600 px for best zoom.
4. **Aspect ratio**: No strict enforcement; typically 1:1 or category-appropriate.
5. **File format**: JPEG preferred; also TIFF, PNG, GIF (non-animated).
6. **Max file size**: 10 MB.
7. **Resolution/DPI**: 72 DPI recommended; longest side ≥1000 px enables Zoom.
8. **Product coverage**: ≥85%.
9. **Background**: Hero must be pure white; supporting images allow lifestyle scenes, text, infographics.
10. **Prohibited elements**: Watermarks, borders, text, logos, URLs, prices, promotions (e.g., "Free Shipping"), non-included accessories, mannequins (except apparel), reviews/ratings, Amazon logos.
11. **Supporting images**: Allow product details, scale references, lifestyle scenes, text overlays, infographics, models.
12. **Video/3D**: 1 video recommended, MP4 or common formats.
13. **Category-specific rules**:
    - Footwear: single shoe facing left at 45°
    - Adult apparel: hero must use model, standing
    - Underwear/swimwear/infant clothing: must be flat lay, no model
14. **Official docs**: [Image requirements](https://sellercentral.amazon.com/help/hub/reference/external/G1881) | [Style guide](https://sellercentral.amazon.com/help/hub/reference/external/G9FUUH87RBNXGKB7)

### Amazon Image Set Style Notes

Amazon is the **info-first** platform. The winning structure for a 4-image set is usually:

1. **White-bg hero** — all colorways in a neat grid or a single clear product shot on pure white; communicates SKU value instantly.
2. **Spec chart** — dimension callouts (diameter, thickness, length, stretch) in one clean infographic slot; merge dimensions rather than spreading them across multiple images.
3. **Usage guide** — 2–4 configurations/positions with short headings and sub-lines showing how the product is used.
4. **Benefit callout** — product arrangement + real hand or lifestyle detail + one benefit sentence; keep it inside the white-bg world.

Style tokens: `clean white background, thin dark gray dimension arrows, small neat sans-serif labels, generous white space, soft neutral shadows`.

> **Why this works**: Amazon buyers are in comparison mode; info density and clarity beat atmospheric lifestyle scenes. Lifestyle can still appear in slots 5–7 if the user wants them, but the core 4-image set should answer "what is it, what size, how do I use it, why is it better" first.

---

### 2. eBay

1. **Image count**: 1–24.
2. **White background**: ⭕ Strongly recommended.
3. **Recommended size**: Min 500×500 px; recommended 1600×1600 px.
4. **Aspect ratio**: 1:1 or 16:9.
5. **File format**: JPEG, PNG, GIF, TIFF, BMP, WEBP, HEIC, AVIF.
6. **Max file size**: 12 MB.
7. **Resolution**: High resolution (1600 px recommended) enables zoom.
8. **Product coverage**: Not specified; must show full product without clutter.
9. **Background**: Neutral or white recommended; avoid cluttered environments.
10. **Prohibited elements**: Borders, text, logos, copyright notices, watermarks, promotional badges.
11. **Supporting images**: Multiple angles, details, defects, size reference (e.g., coin), package contents.
12. **Video**: Via Photos & Video panel.
13. **Category-specific**: Used/vintage items must use actual photos (no stock images); PSA Graded Cards have a dedicated auto-fill flow.
14. **Official docs**: [Picture requirements](https://www.ebay.com/help/selling/listings/adding-pictures-listings/picture-requirements?id=4148)

### eBay Image Set Style Notes

eBay is the **trust-first catalog** platform. The safest 4-image set is a consistent light-gray or white multi-view catalog:

1. **3/4 hero** — angled front view on neutral background.
2. **Flat front view** — straight-on for shape verification.
3. **Edge/profile/detail view** — thickness, texture, or mechanism.
4. **Macro or lineup** — material close-up or scale reference (e.g., coin, hand).

Style tokens: `light gray seamless background, even soft shadow, consistent camera height and lighting across all views, no props that do not come with the product`.

> **Why this works**: eBay buyers need to verify condition and authenticity; multi-view consistency is the trust signal. This set is a solid fallback when the user does not need a styled campaign, but it is usually lower priority than Amazon/TikTok/Etsy/Shopify sets.

---

### 3. Walmart Marketplace

1. **Image count**: ≥1; recommended ≥4.
2. **White background**: ✅ Mandatory seamless pure white (RGB 255, 255, 255).
3. **Recommended size**: US: 2200×2200 px; CA: 2000×2000 px @ 300 ppi.
4. **Aspect ratio**: 1:1 (square).
5. **File format**: JPEG, JPG, PNG, BMP (no animated GIF).
6. **Max file size**: US: 5 MB; CA: 1 MB.
7. **Resolution**: Min 500×500 px (below = auto-delist); zoom requires 1500×1500 (US) / 2000×2000 (CA).
8. **Product coverage**: Fill frame as closely as possible.
9. **Background**: Hero must be seamless white; supporting images allow environment/detail shots.
10. **Prohibited elements**: Watermarks, personal/company logos, text overlays, promotional language, price tags, borders, non-included accessories, other retailer logos.
11. **Supporting images**: Back, side, detail, multi-angle, lifestyle; apparel allows "Pack Bugs".
12. **Video/3D**: Rich Media supported per Walmart media library standards.
13. **Category-specific**: Large items (e.g., bedding) may include reasonable lifestyle environment.
14. **Official docs**: [US guidelines](https://marketplacelearn.walmart.com/guides/Item%20setup/Item%20content,%20imagery,%20and%20media/Product-detail-page:-Image-guidelines-&-requirements) | [CA guidelines](https://marketplacelearn.walmart.com/ca/guides/Item%20setup/Item%20content,%20imagery,%20and%20media/item-image-guidelines)

---

### 4. Shopify

1. **Image count**: Min 1; max 250 media items per product (images + 3D + video).
2. **White background**: Not required; theme-dependent.
3. **Recommended size**: 2048×2048 px (square) for best display.
4. **Aspect ratio**: 1:1 recommended; any ratio supported (auto-generates thumbnails).
5. **File format**: PNG (preferred), JPEG, WebP, PSD, TIFF, BMP, GIF, SVG, HEIC.
6. **Max file size**: Image 20 MB; 3D model 500 MB; video 1 GB.
7. **Resolution**: Max 5000×5000 px or 25 megapixels.
8. **Product coverage**: Not specified; product should be clear.
9. **Background**: Not specified; driven by merchant brand style.
10. **Prohibited elements**: Embedded videos must not use private/restricted-access videos (must be public/unlisted).
11. **Supporting images**: Encouraged multi-angle; system auto-generates size variants.
12. **Video/3D**: Video ≤10 min, up to 4K, .mp4/.mov/.webm; 3D: .GLB/.USDZ.
13. **Category-specific**: None (customized via apps/theme code).
14. **Official docs**: [Product media types](https://help.shopify.com/en/manual/products/product-media/product-media-types) | [Add media](https://help.shopify.com/en/manual/products/product-media/add-media)

### Shopify Image Set Style Notes

Shopify is the **brand-owned premium** platform. There is no platform-imposed style, so the image world should match the merchant's page theme. A proven 4-image luxury/professional set:

1. **Product portrait** — negative-space hero under deliberate lighting (spotlight or rim light).
2. **Detail macro** — material, weave, texture, or craft close-up.
3. **Styled flat lay** — curated surface arrangement showing the product as object.
4. **Gift box / packaging shot** — only when gifting is the selling angle; otherwise replace with another detail or lifestyle still.

Style tokens: `dark moody premium, deliberate lighting design, controlled shadows, styled surfaces, editorial composition, subtle film grain`.

Hard boundary vs TikTok: **people are absent, or at most one anonymous styling hand**. The product-as-object is the selling point; it should feel like a brand lookbook, not a creator post.

> **Why this works**: Shopify shoppers buy into a brand world. Coherence between the image set and the page theme (dark set → dark theme, serif wordmark, swatch dots) is the conversion driver. Adapt the palette to the product category (navy/teal for premium, warm earth for artisan, monochrome for minimalist).

---

### 5. Etsy

1. **Image count**: Max 10.
2. **White background**: Not required; stock images and placeholder renders prohibited.
3. **Recommended size**: Shortest side ≥2000 px; first image width/height ≥635 px (affects search ranking).
4. **Aspect ratio**: 4:3 or 1:1 recommended (first image horizontal or square for thumbnail cropping).
5. **File format**: .jpg, .gif, .png, .svg, .heic (no animated .gif, no transparent .png).
6. **Max file size**: ≤1 MB recommended for stable upload.
7. **Resolution**: 72 PPI recommended; sRGB color mode.
8. **Product coverage**: Centered with adequate negative space (for cropping tolerance).
9. **Background**: Clean with ample whitespace recommended.
10. **Prohibited elements**: Hero must not contain placeholder mockups (e.g., "Your Text Here"); must use original photos.
11. **Supporting images**: Subsequent images may use renders to show customization options.
12. **Video**: 3–15 s, silent, ≤100 MB, MP4/MOV, 1080p recommended.
13. **Category-specific**: Children's products must meet safety policy; custom products require real sample as hero.
14. **Official docs**: [Image help](https://help.etsy.com/hc/en-us/articles/115015663347) | [Image requirements](https://www.etsy.com/legal/policy/listing-image-requirements/253962679005)

### Etsy Image Set Style Notes

Etsy is the **handmade / warm / gifting** platform. The winning system is a handwritten scrapbook collage set, all in **4:3 landscape** with content kept in the central safe zone (desktop thumbnails crop 4:3, mobile crops 1:1).

A proven 4-image structure:

1. **Cover** — product cluster or wreath on one side, large handwritten script title + subline on the other; title must survive thumbnail crop.
2. **Feature chart** — products or details annotated with handwritten color names and small icons.
3. **Usage polaroids** — 1–2 taped photo frames showing the product in use, with warm annotation.
4. **Gift box** — kraft box with tissue/twine, product peeking out; box itself stays text-free, annotation around it.

Style tokens: `textured light gray paper background, beige washi tape, elegant handwritten script typography, hand-drawn hearts/arrows/sparkles, polaroid frames, soft natural shadows`.

> **Why this works**: The handwritten annotation layer carries the 手作感 and personality that plain photography cannot. Do not abandon it because a user complains the template feels repetitive — instead vary the collage dialect (torn-edge scraps, filmstrips, notebook margins, wax seals, pressed flowers) while keeping the handwriting voice.

---

### 6. AliExpress

1. **Image count**: 1–6 (some categories up to 8).
2. **White background**: Mandatory for first hero image.
3. **Recommended size**: ≥800×800 px.
4. **Aspect ratio**: 1:1.
5. **File format**: JPG, JPEG, PNG.
6. **Max file size**: ≤2 MB or ≤5 MB (varies by category).
7. **Resolution**: Not specified; must be clear and not blurry.
8. **Product coverage**: 70%–85%.
9. **Background**: First image pure white; subsequent allow solid color, scene, or lifestyle.
10. **Prohibited elements**: Borders, watermarks, multi-image collages, oversized marketing text or color blocks.
11. **Supporting images**: Recommended order: front, back, side, detail, scene, packaging.
12. **Video**: ≤30 s (max 2 min), ≤2 GB, AVI/3GP/MOV/MP4.
13. **Category-specific**: Apparel recommends model photography.
14. **Official docs**: [Seller portal](https://sell.aliexpress.com/) | [Seller learning](https://sellerlearning.aliexpress.com/)

---

### 7. TikTok Shop

1. **Image count**: 1–9 (≥5 recommended for "Good" quality rating).
2. **White background**: Hero (first image) must be pure white.
3. **Recommended size**: ≥600×600 px.
4. **Aspect ratio**: 1:1 (square).
5. **File format**: JPG, JPEG, PNG.
6. **Max file size**: Image not specified (≤2 MB recommended); video ≤5 MB.
7. **Resolution**: >600×600 px.
8. **Product coverage**: Not specified; must clearly show the subject.
9. **Background**: Hero must be pure white; no mosaic or blur effects.
10. **Prohibited elements**: Watermarks, text, borders, graphic overlays, marketing stickers (e.g., "Best Seller"), digital renders, black-and-white images.
11. **Supporting images**: Show different angles, functional details, accessories; no duplicate angles.
12. **Video specs**: Max 1 video per listing, **≤5 MB** (very strict limit).
13. **Category-specific**: Food must show packaging; children's swimwear/underwear must be flat lay on background — no live models or mannequins.
14. **Official docs**: [Image guidelines](https://seller-us.tiktok.com/university/essay?knowledge_id=3196690250417921)

### TikTok Shop Image Set Style Notes

TikTok Shop is the **creator / UGC 种草** platform. The hard differentiator is **human presence**: at least 3 of the 4 shots must include a person or body part (hands, arms, lap, shoulder, POV grip) **interacting with the product**.

Recommended 4-image structure:

1. **Worn-as-accessory close-up** — hands, wrist stack, clasped pose, or how the product is worn/held.
2. **Rear-head / hairstyle detail** — product in use with hard shadow on wall; faces out of frame.
3. **Mid-motion freeze** — the core benefit shown in action (secure hold, stretch, grip, etc.).
4. **Pre-activity ritual** — lacing shoes, gear bench, getting-ready moment; product in life, not on a pedestal.

Style tokens (sports / active categories): `direct camera flash aesthetic, hard small shadows, slightly grainy editorial film look, cool tones, plain unbranded garments, faces out of frame`.

Style tokens (cozy / home categories): `warm golden light, cream/beige home scenes, influencer phone-photo authenticity, lived-in mess, soft natural skin tones`.

Hard rules:
- **Ratio: 1:1 square ONLY (800×800+)** — never 3:4/4:5, even if official docs are silent on ratio.
- **No text overlays** on the editorial set; let the action do the selling.
- **AI-original people only**, faces cropped or out of frame; plain unbranded garments; no crests/numbers/athlete likeness.

Boundary vs Shopify: TikTok should feel like *"a creator I follow just posted this"*; Shopify should feel like *"a brand's lookbook page"*. If a generated TikTok set could pass as Shopify, regenerate with stronger human presence and phone-photo angles.

> **Why this works**: TikTok shoppers convert on authentic "someone like me uses it" energy. The person USING the product is the selling point, not the product alone.

---

### 8. Shopee

1. **Image count**: 1–9 (including cover); Shopee Mall requires ≥3 different angles.
2. **White background**: Cover image requires solid-color background (white preferred).
3. **Recommended size**: Mall min 500×500 px; recommended 1024×1024 px.
4. **Aspect ratio**: 1:1 mandatory; optional 3:4 upload for extra traffic.
5. **File format**: JPG, JPEG, PNG.
6. **Max file size**: ≤2 MB.
7. **Resolution**: Must be clear, sharp, true-color.
8. **Product coverage**: Cover ≥60%; non-cover ≥50%.
9. **Background**: Cover must be solid color (white preferred); apparel/food/home non-cover may use environment backgrounds.
10. **Prohibited elements**: Watermarks, collages, borders, promotional text/symbols; Mall seller logo limited to top-left corner at <10% area.
11. **Supporting images**: Must show different angles, specs, usage.
12. **Video**: Max 1, ≤30 MB, resolution ≤1280×1280, 10–60 s, MP4.
13. **Category-specific**: Fashion/beauty cover allows models; adult products require special coverage guidelines.
14. **Official docs**: [Image guide](https://seller.shopee.sg/edu/article/34)

---

### 9. Lazada

1. **Image count**: 3–8.
2. **White background**: Hero (first image) must be pure white.
3. **Recommended size**: Min 330×330 px; recommended 1000×1000 or 1600×1600 px.
4. **Aspect ratio**: 1:1.
5. **File format**: JPG, JPEG, PNG.
6. **Max file size**: ≤3 MB.
7. **Resolution**: ≥72 DPI.
8. **Product coverage**: ~80% (e.g., 80–100 px margin on 1600 px canvas).
9. **Background**: Hero must be pure white (RGB 255, 255, 255).
10. **Prohibited elements**: Watermarks, promotional text, borders, graphic overlays.
11. **Supporting images**: Must include side, back, detail views; lifestyle scenes or scale references recommended.
12. **Video**: ≤100 MB, 10–60 s, MP4.
13. **Category-specific**: Fashion supports AI model try-on generated images.
14. **Official docs**: [Lazada University](https://university.lazada.sg/) | [Image requirements](https://redmart.lazada.sg/seller/support/image-requirements-12698.html)

---

## B2B

### 10. Alibaba.com (International)

Use this spec when generating an Alibaba.com main image set (主图套图) so the output meets the platform's upload rules and risk-control requirements.

**1. Set composition — FIXED, MANDATORY 6 images (not optional):** every Alibaba.com main image set MUST always generate exactly these 6 images, in this order. Do not skip, substitute, reorder, add, or reduce any of them:
- White-background image ×1 (must be the first/hero image)
- Scene image ×1
- Detail image ×1
- Marketing selling-point image ×1
- Production/process image ×1
- Model image ×1

**2. Base image parameters:**
- Recommended size: not smaller than 640×640; recommended 1000×1000 square.
- Aspect ratio: square (1:1), edge length within 1000×1000.
- File format: JPG / JPEG / PNG.
- File size: ≤5 MB per image.

**3. Hero (white-background) requirements:**
- Mandatory white-background real-product photo: the hero must be a pure-white-background real product shot (also applies to customized products); 3D renders are strictly prohibited.
- Complete and clear subject: the product subject must not be missing or cropped; do not use detail/close-up/partial shots; the subject must be clear with visible details, not too small or blurry.

**4. Composition:**
- Subject coverage: product occupies 75%–80% of the frame, clear and centered.
- No collage, no borders: image collages are not allowed; borders of any form are not allowed, including "white-border images" created by pasting the original onto a white canvas to force a ratio (judged as a border issue — use a ratio-adjustment tool instead).

**5. Copy & language:**
- Any text in the image must be in English.
- Chinese text is strictly prohibited.

**6. Prohibited elements — none of the following may appear:**
- Contact info (including WeChat ID), URLs, QR codes.
- Watermarks (including video watermarks, text watermarks, and watermarks of any form).
- Marketing / discount / platform-benefit wording, including but not limited to: `Local stock`, `EU Local stock`, `Fast customization`, `Guaranteed`, `certified`, `MARCH`, `FREE shipping`, `US$20 off of shipping`, `50% off`, `30% off of new buyers`, `every ¥15 off 15`, `( )% tariff support`, `180-day lowest price`, `delivery`, `dispatch`, `delivery by`, `lower tariff`, `1-year-warranty`, `easy return / money back guarantee`, `GMV`, `1 popular in jewelry`.

**7. Risk-control compliance:**
- Must not contain pornographic, violent, political, terrorist, gory, prohibited-goods, vulgar, or sensitive content; violations will be rejected by the risk-control model after submission.

**8. Official docs**: [Rules](https://rule.alibaba.com/rule/detail/11000682.htm) | [Knowledge base](https://service.alibaba.com/page/knowledge?pageId=128&category=1000000021)

---

### 11. 1688

1. **Image count**: **≥5** (1 hero + 4 supporting) — key indicator for product quality score.
2. **White background**: Hero must be white-background real product photo (no 3D renders for customizable items).
3. **Recommended size**: ≥800×800 px.
4. **Aspect ratio**: Strict 1:1.
5. **File format**: JPG / JPEG / PNG.
6. **Max file size**: ≤5 MB per image.
7. **Resolution**: Industrial/technical drawings ≥150 dpi; critical dimensions labeled in mm.
8. **Product coverage**: 75%–80%, centered and clear.
9. **Background**: Pure white (RGB 255, 255, 255), no shadow or very faint shadow.
10. **Prohibited elements**: Watermarks (especially those from other platforms), messaging QR codes, external links, excessive text overlays.
11. **Detail page images**: Width ≤752 px (max 790 px); include material, craft, size chart, factory capability modules.
12. **Video**: MP4, ≤30 s, ≥720P, must showcase core selling points or production process.
13. **Category-specific**: Apparel — hero must cover front/side/detail; hardware/electronics — hero should include technical parameters or drawings.
14. **Official docs**: [Rules](https://rule.1688.com/) | [Wiki](https://wiki.1688.com/zh/WKfkh560fqu60w)
