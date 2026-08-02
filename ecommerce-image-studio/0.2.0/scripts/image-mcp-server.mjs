import { mkdir, readFile, writeFile } from "node:fs/promises";
import { dirname, extname, basename, resolve } from "node:path";

const MAX_FILE_BYTES = 25 * 1024 * 1024;
const MIME_TYPES = new Map([[".png", "image/png"], [".jpg", "image/jpeg"], [".jpeg", "image/jpeg"], [".webp", "image/webp"]]);
const DEFAULT_PROVIDER = { baseUrl: "https://api.openai.com/v1", model: "gpt-image-2" };
const SUPPORTED_MODELS = ["gpt-image-2", "gpt-image-2-1.5k", "gpt-image-1.5", "gpt-image-1"];
const FALLBACK_MODEL = "gpt-image-1.5";

const tools = [
  {
    name: "health_check",
    description: "Report image-provider configuration, selected model, and compatible fallback guidance without making a billable image request.",
    inputSchema: { type: "object", additionalProperties: false, properties: {} }
  },
  {
    name: "image_generate",
    description: "Generate an ecommerce product image. Returns the generated image directly to Codex.",
    inputSchema: {
      type: "object",
      additionalProperties: false,
      required: ["prompt"],
      properties: {
        prompt: { type: "string", minLength: 1, maxLength: 8000, description: "Detailed image brief." },
        size: { type: "string", enum: ["1024x1024", "1024x1536", "1536x1024", "auto"], default: "auto" },
        quality: { type: "string", enum: ["low", "medium", "high", "auto"], default: "auto" },
        background: { type: "string", enum: ["transparent", "opaque", "auto"], default: "auto" },
        model: { type: "string", enum: SUPPORTED_MODELS, description: "Optional image model override." },
        output_path: { type: "string", description: "Optional local PNG destination. The image is also returned to Codex." }
      }
    }
  },
  {
    name: "image_edit",
    description: "Edit a local product image using an instruction. Optional mask marks editable pixels.",
    inputSchema: {
      type: "object",
      additionalProperties: false,
      required: ["image_path", "prompt"],
      properties: {
        image_path: { type: "string", minLength: 1, description: "Absolute or workspace-relative path to PNG, JPEG, or WebP." },
        mask_path: { type: "string", description: "Optional PNG mask; transparent pixels are editable." },
        prompt: { type: "string", minLength: 1, maxLength: 8000, description: "Precise edit instruction." },
        size: { type: "string", enum: ["1024x1024", "1024x1536", "1536x1024", "auto"], default: "auto" },
        quality: { type: "string", enum: ["low", "medium", "high", "auto"], default: "auto" },
        background: { type: "string", enum: ["transparent", "opaque", "auto"], default: "auto" },
        model: { type: "string", enum: SUPPORTED_MODELS, description: "Optional image model override." },
        input_fidelity: { type: "string", enum: ["low", "high"], description: "Optional preservation level. Not supported by gpt-image-2." },
        output_path: { type: "string", description: "Optional local PNG destination. The image is also returned to Codex." }
      }
    }
  }
];

function response(id, result) { return { jsonrpc: "2.0", id, result }; }
function failure(id, code, message) { return { jsonrpc: "2.0", id, error: { code, message } }; }
function toolFailure(message) { return { content: [{ type: "text", text: message }], isError: true }; }
function endpoint(provider, suffix) { return `${provider.baseUrl.replace(/\/$/, "")}${suffix}`; }

function selectedModel(args, provider) { return args.model ?? provider.model; }

function validateRequest(args, provider) {
  const model = selectedModel(args, provider);
  if (model === "gpt-image-2" && args.background === "transparent") {
    throw new Error("gpt-image-2 does not support transparent output. Ask the user before retrying with model gpt-image-1.5 and background transparent.");
  }
  if (model === "gpt-image-2" && args.input_fidelity) {
    throw new Error("gpt-image-2 always uses high input fidelity; omit input_fidelity.");
  }
}

function getProvider() {
  const apiKey = process.env.EIS_PROVIDER_API_KEY ?? process.env.OPENAI_API_KEY;
  if (!apiKey) throw new Error("Set EIS_PROVIDER_API_KEY or OPENAI_API_KEY in Codex's process environment.");
  const baseUrl = process.env.EIS_PROVIDER_BASE_URL ?? process.env.OPENAI_BASE_URL ?? DEFAULT_PROVIDER.baseUrl;
  if (!/^https:\/\//.test(baseUrl)) throw new Error("EIS_PROVIDER_BASE_URL or OPENAI_BASE_URL must be an HTTPS URL.");
  const model = process.env.EIS_PROVIDER_MODEL ?? process.env.OPENAI_IMAGE_MODEL ?? DEFAULT_PROVIDER.model;
  if (!SUPPORTED_MODELS.includes(model)) throw new Error(`OPENAI_IMAGE_MODEL must be one of: ${SUPPORTED_MODELS.join(", ")}.`);
  return {
    apiKey,
    baseUrl,
    model
  };
}

async function getLocalImage(path, fieldName) {
  const extension = extname(path).toLowerCase();
  const mimeType = MIME_TYPES.get(extension);
  if (!mimeType) throw new Error(`${fieldName} must be a PNG, JPEG, or WebP file.`);
  const buffer = await readFile(path);
  if (!buffer.length || buffer.length > MAX_FILE_BYTES) throw new Error(`${fieldName} must be between 1 byte and 25 MB.`);
  return { buffer, mimeType, filename: basename(path) };
}

async function providerRequest(provider, path, options) {
  const request = await fetch(endpoint(provider, path), {
    ...options,
    headers: { Authorization: `Bearer ${provider.apiKey}`, ...options.headers }
  });
  if (!request.ok) {
    const detail = (await request.text()).slice(0, 1000);
    const fallback = request.status === 503 && provider.model === "gpt-image-2"
      ? ` The gpt-image-2 channel is unavailable. Ask the user for confirmation, then retry with model ${FALLBACK_MODEL}.`
      : "";
    throw new Error(`Image provider returned ${request.status}: ${detail || request.statusText}.${fallback}`);
  }
  const payload = await request.json();
  const image = payload?.data?.[0];
  if (!image?.b64_json) throw new Error("Image provider did not return base64 image data. Configure a provider compatible with the OpenAI Images API.");
  return { data: image.b64_json, revisedPrompt: image.revised_prompt };
}

async function imageResult(image, outputPath) {
  const content = [{ type: "image", data: image.data, mimeType: "image/png" }];
  if (image.revisedPrompt) content.unshift({ type: "text", text: `Provider revised prompt: ${image.revisedPrompt}` });
  if (outputPath) {
    const destination = resolve(outputPath);
    await mkdir(dirname(destination), { recursive: true });
    await writeFile(destination, Buffer.from(image.data, "base64"));
    content.unshift({ type: "text", text: `Saved generated image: ${destination}` });
  }
  return { content };
}

function providerHealth() {
  const apiKey = process.env.EIS_PROVIDER_API_KEY ?? process.env.OPENAI_API_KEY;
  const baseUrl = process.env.EIS_PROVIDER_BASE_URL ?? process.env.OPENAI_BASE_URL ?? DEFAULT_PROVIDER.baseUrl;
  const model = process.env.EIS_PROVIDER_MODEL ?? process.env.OPENAI_IMAGE_MODEL ?? DEFAULT_PROVIDER.model;
  const issues = [];
  if (!apiKey) issues.push("Set EIS_PROVIDER_API_KEY or OPENAI_API_KEY, then restart Codex.");
  if (!/^https:\/\//.test(baseUrl)) issues.push("EIS_PROVIDER_BASE_URL or OPENAI_BASE_URL must be an HTTPS URL.");
  if (!SUPPORTED_MODELS.includes(model)) issues.push(`OPENAI_IMAGE_MODEL must be one of: ${SUPPORTED_MODELS.join(", ")}.`);
  return { configured: issues.length === 0, apiKeyPresent: Boolean(apiKey), baseUrl, model, fallbackModel: FALLBACK_MODEL, issues };
}

function generationBody(args, provider) {
  return JSON.stringify({ model: args.model ?? provider.model, prompt: args.prompt, size: args.size ?? "auto", quality: args.quality ?? "auto", background: args.background ?? "auto", response_format: "b64_json" });
}

async function callTool(name, args) {
  if (name === "health_check") return { content: [{ type: "text", text: JSON.stringify(providerHealth()) }] };
  const provider = getProvider();
  validateRequest(args, provider);
  if (name === "image_generate") return imageResult(await providerRequest(provider, "/images/generations", { method: "POST", headers: { "Content-Type": "application/json" }, body: generationBody(args, provider) }), args.output_path);
  if (name === "image_edit") {
    const image = await getLocalImage(args.image_path, "image_path");
    const form = new FormData();
    form.append("model", selectedModel(args, provider));
    form.append("prompt", args.prompt);
    form.append("image", new Blob([image.buffer], { type: image.mimeType }), image.filename);
    if (args.mask_path) {
      const mask = await getLocalImage(args.mask_path, "mask_path");
      form.append("mask", new Blob([mask.buffer], { type: mask.mimeType }), mask.filename);
    }
    for (const name of ["size", "quality", "background"]) if (args[name]) form.append(name, args[name]);
    if (args.input_fidelity) form.append("input_fidelity", args.input_fidelity);
    form.append("response_format", "b64_json");
    return imageResult(await providerRequest(provider, "/images/edits", { method: "POST", body: form }), args.output_path);
  }
  throw new Error(`Unknown tool: ${name}`);
}

async function handle(message) {
  if (message.method === "notifications/initialized") return null;
  if (message.method === "initialize") return response(message.id, { protocolVersion: "2024-11-05", capabilities: { tools: {} }, serverInfo: { name: "ecommerce-image-studio", version: "0.2.0" } });
  if (message.method === "ping") return response(message.id, {});
  if (message.method === "tools/list") return response(message.id, { tools });
  if (message.method === "tools/call") {
    try { return response(message.id, await callTool(message.params?.name, message.params?.arguments ?? {})); }
    catch (error) { return response(message.id, toolFailure(`Image request failed: ${error.message}`)); }
  }
  return failure(message.id, -32601, `Method not found: ${message.method}`);
}

let buffer = "";
process.stdin.setEncoding("utf8");
process.stdin.on("data", async (chunk) => {
  buffer += chunk;
  let delimiter;
  while ((delimiter = buffer.indexOf("\n")) !== -1) {
    const line = buffer.slice(0, delimiter).trim();
    buffer = buffer.slice(delimiter + 1);
    if (!line) continue;
    try { const result = await handle(JSON.parse(line)); if (result) process.stdout.write(`${JSON.stringify(result)}\n`); }
    catch (error) { process.stdout.write(`${JSON.stringify(failure(null, -32700, error.message))}\n`); }
  }
});
