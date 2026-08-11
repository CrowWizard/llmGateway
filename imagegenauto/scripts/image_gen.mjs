#!/usr/bin/env node
import { access, mkdir, readFile, writeFile } from "node:fs/promises";
import { basename, dirname, extname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptsDirectory = dirname(fileURLToPath(import.meta.url));
const runtimeModule = resolve(scriptsDirectory, "..", "..", "noderuntime", "scripts", "runtime.mjs");

const DEFAULT_MODEL = process.env.OPENAI_IMAGE_MODEL ?? "gpt-image-2";
const MODELS = ["gpt-image-2", "gpt-image-1.5", "gpt-image-1"];
const TRANSIENT_STATUS = new Set([408, 409, 429, 500, 502, 503, 504]);
const DEFAULT_OUT = "output/imagegen/output.png";

class ProviderError extends Error {
  constructor(status, detail) {
    super(`Image provider returned ${status}: ${detail}`);
    this.status = status;
    this.detail = detail;
  }
}

function die(message) { throw new Error(message); }

function parseArgs(argv) {
  const [command, ...rest] = argv;
  if (!["generate", "edit", "generate-batch"].includes(command)) die("Command must be generate, edit, or generate-batch.");
  const args = { command, model: DEFAULT_MODEL, provider: "auto", n: 1, size: "auto", quality: "medium", out: DEFAULT_OUT, augment: true, images: [], concurrency: 3 };
  for (let i = 0; i < rest.length; i += 1) {
    const token = rest[i];
    if (token === "--force" || token === "--dry-run" || token === "--no-augment" || token === "--fail-fast") {
      args[token.slice(2).replaceAll("-", "_")] = token === "--no-augment" ? false : true;
      continue;
    }
    const key = token.replace(/^--/, "").replaceAll("-", "_");
    if (!token.startsWith("--") || i + 1 >= rest.length) die(`Invalid argument: ${token}`);
    const value = rest[++i];
    if (key === "image") args.images.push(value);
    else args[key] = ["n", "output_compression", "concurrency", "max_attempts"].includes(key) ? Number(value) : value;
  }
  if (!Number.isInteger(args.concurrency) || args.concurrency < 1 || args.concurrency > 5) die("--concurrency must be an integer from 1 to 5.");
  return args;
}

function valueOrUndefined(value) { return value === undefined || value === "" ? undefined : value; }

function promptFor(args, job) {
  const prompt = job?.prompt ?? args.prompt;
  if (!prompt && !args.prompt_file) die("Missing prompt. Use --prompt or --prompt-file.");
  return args.prompt_file ? readFile(args.prompt_file, "utf8").then(text => text.trim()) : Promise.resolve(String(prompt).trim());
}

function augmentPrompt(args, prompt, job = {}) {
  if (args.augment === false) return prompt;
  const fields = ["use_case", "scene", "subject", "style", "composition", "lighting", "palette", "materials", "text", "constraints", "negative"];
  const sections = [`Primary request: ${prompt}`];
  for (const field of fields) {
    const value = job[field] ?? args[field];
    if (value) sections.push(`${field === "use_case" ? "Use case" : field}: ${field === "text" ? `"${value}"` : value}`);
  }
  return sections.length ? sections.join("\n") : prompt;
}

function provider() {
  const apiKey = process.env.OPENAI_API_KEY;
  if (!apiKey) die("OPENAI_API_KEY is not set. Export it before running.");
  const baseUrl = process.env.OPENAI_BASE_URL ?? "https://api.openai.com/v1";
  if (!/^https?:\/\//i.test(baseUrl)) die("OPENAI_BASE_URL must be an HTTP(S) URL.");
  return { apiKey, baseUrl: baseUrl.replace(/\/$/, "") };
}

function providerFor(args, model) {
  const providerName = args.provider === "auto" ? (model.startsWith("gemini-") ? "gemini" : "openai") : args.provider;
  if (providerName === "gemini") {
    const apiKey = process.env.GEMINI_API_KEY ?? process.env.GOOGLE_API_KEY ?? process.env.OPENAI_API_KEY;
    if (!apiKey) die("GEMINI_API_KEY, GOOGLE_API_KEY, or OPENAI_API_KEY is not set. Export one before running.");
    const baseUrl = process.env.GEMINI_BASE_URL ?? process.env.OPENAI_BASE_URL ?? "https://generativelanguage.googleapis.com/v1beta";
    if (!/^https?:\/\//i.test(baseUrl)) die("GEMINI_BASE_URL must be an HTTP(S) URL.");
    return { name: providerName, apiKey, baseUrl: baseUrl.replace(/\/$/, "") };
  }
  if (providerName !== "openai") die(`Unsupported provider: ${providerName}. Use auto, openai, or gemini.`);
  return { name: providerName, ...provider() };
}

function providerNameFor(args, model) {
  return args.provider === "auto" ? (model.startsWith("gemini-") ? "gemini" : "openai") : args.provider;
}

async function requestJson(client, path, body) {
  const response = await fetch(`${client.baseUrl}${path}`, { method: "POST", headers: { Authorization: `Bearer ${client.apiKey}`, "Content-Type": "application/json" }, body: JSON.stringify(body) });
  if (!response.ok) throw new ProviderError(response.status, (await response.text()).slice(0, 1000));
  return response.json();
}

async function requestEdit(client, args, payload) {
  const form = new FormData();
  for (const [key, value] of Object.entries(payload)) if (value !== undefined) form.append(key, String(value));
  for (const image of args.images) form.append("image", new Blob([await readFile(image)]), basename(image));
  if (args.mask) form.append("mask", new Blob([await readFile(args.mask)]), basename(args.mask));
  const response = await fetch(`${client.baseUrl}/images/edits`, { method: "POST", headers: { Authorization: `Bearer ${client.apiKey}` }, body: form });
  if (!response.ok) throw new ProviderError(response.status, (await response.text()).slice(0, 1000));
  return response.json();
}

async function requestGemini(client, model, prompt) {
  const response = await fetch(`${client.baseUrl}/models/${encodeURIComponent(model)}:generateContent?key=${encodeURIComponent(client.apiKey)}`, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({
      contents: [{ role: "user", parts: [{ text: prompt }] }],
      generationConfig: { responseModalities: ["TEXT", "IMAGE"] }
    })
  });
  if (!response.ok) throw new ProviderError(response.status, (await response.text()).slice(0, 1000));
  const result = await response.json();
  const images = [];
  for (const part of result.candidates?.flatMap(candidate => candidate.content?.parts ?? []) ?? []) {
    const data = part.inlineData?.data ?? part.inline_data?.data;
    if (data) images.push(data);
  }
  return images;
}

async function outputPaths(args, format, count) {
  const batchName = args.batch_index === undefined ? "image_1" : `image_${args.batch_index + 1}`;
  const base = args.out_dir ? `${args.out_dir}/${batchName}.${format}` : args.out;
  const normalized = extname(base) ? base : `${base}.${format}`;
  return count === 1 ? [normalized] : Array.from({ length: count }, (_, i) => normalized.replace(/(\.[^.]*)$/, `-${i + 1}$1`));
}

function downscaledPath(path, suffix) {
  const extension = extname(path);
  return `${path.slice(0, -extension.length)}${suffix}${extension}`;
}

async function saveImages(data, paths, args) {
  const sharp = args.downscale_max_dim ? await import(runtimeModule).then(module => module.importSharedPackage("sharp")) : null;
  for (let i = 0; i < Math.min(data.length, paths.length); i += 1) {
    if (!args.force && await access(paths[i]).then(() => true, () => false)) {
      die(`Output already exists: ${paths[i]} (use --force to overwrite)`);
    }
    await mkdir(dirname(paths[i]), { recursive: true });
    const image = Buffer.from(data[i], "base64");
    await writeFile(paths[i], image);
    console.log(`Wrote ${paths[i]}`);
    if (sharp) {
      const resizedPath = downscaledPath(paths[i], args.downscale_suffix ?? "-web");
      if (!args.force && await access(resizedPath).then(() => true, () => false)) die(`Output already exists: ${resizedPath} (use --force to overwrite)`);
      await sharp(image).resize({ width: args.downscale_max_dim, height: args.downscale_max_dim, fit: "inside", withoutEnlargement: true }).toFile(resizedPath);
      console.log(`Wrote ${resizedPath}`);
    }
  }
}

function payloadFor(args, prompt, model) {
  const payload = { model, prompt, n: args.n, size: args.size, quality: args.quality, background: valueOrUndefined(args.background), output_format: args.output_format ?? "png", output_compression: args.output_compression, input_fidelity: args.input_fidelity, moderation: args.moderation };
  if (model === "gpt-image-2" && args.background === "transparent") die("gpt-image-2 does not support transparent output; use gpt-image-1.5.");
  return Object.fromEntries(Object.entries(payload).filter(([, value]) => value !== undefined));
}

async function runOne(args, model, job) {
  const effectiveArgs = { ...args, ...job, prompt: job?.prompt ?? args.prompt, images: job?.images ?? args.images };
  const prompt = augmentPrompt(effectiveArgs, await promptFor(effectiveArgs), effectiveArgs);
  const payload = payloadFor(effectiveArgs, prompt, model);
  const paths = await outputPaths(effectiveArgs, payload.output_format, effectiveArgs.n);
  const downscaled = effectiveArgs.downscale_max_dim ? paths.map(path => downscaledPath(path, effectiveArgs.downscale_suffix ?? "-web")) : undefined;
  const providerName = providerNameFor(effectiveArgs, model);
  const endpoint = providerName === "gemini" ? `/models/${model}:generateContent` : effectiveArgs.command === "edit" ? "/v1/images/edits" : "/v1/images/generations";
  if (effectiveArgs.dry_run) { console.log(JSON.stringify({ provider: providerName, endpoint, outputs: paths, outputs_downscaled: downscaled, ...payload }, null, 2)); return; }
  if (providerName === "gemini" && effectiveArgs.n !== 1) die("Gemini generateContent image generation currently supports exactly one image; use --n 1.");
  const client = providerFor(effectiveArgs, model);
  if (client.name === "gemini" && effectiveArgs.command === "edit") die("Gemini provider currently supports generate only; use --provider openai for image edits.");
  const images = client.name === "gemini"
    ? await requestGemini(client, model, prompt)
    : (effectiveArgs.command === "edit" ? await requestEdit(client, effectiveArgs, payload) : await requestJson(client, "/images/generations", payload)).data?.map(item => item.b64_json).filter(Boolean) ?? [];
  if (!images.length) die("Image provider did not return base64 image data.");
  await saveImages(images, paths, effectiveArgs);
}

async function runBatch(args, jobs) {
  let nextJobIndex = 0;
  let firstError;
  const workerCount = Math.min(args.concurrency, jobs.length);

  async function worker(workerIndex) {
    while (firstError === undefined) {
      const batchIndex = nextJobIndex;
      nextJobIndex += 1;
      if (batchIndex >= jobs.length) return;
      const job = jobs[batchIndex];
      console.error(`[imagegen] worker ${workerIndex + 1}: starting job ${batchIndex + 1}/${jobs.length}`);
      try {
        await runOne({ ...args, command: "generate", batch_index: batchIndex }, job.model ?? args.model, job);
      } catch (error) {
        firstError = error;
      }
    }
  }

  await Promise.all(Array.from({ length: workerCount }, (_, workerIndex) => worker(workerIndex)));
  if (firstError) throw firstError;
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  if (args.command === "generate-batch") {
    if (!args.input || !args.out_dir) die("generate-batch requires --input and --out-dir.");
    const jobs = (await readFile(args.input, "utf8")).split(/\r?\n/).filter(line => line.trim() && !line.trim().startsWith("#")).map(line => line.trim().startsWith("{") ? JSON.parse(line) : { prompt: line });
    await runBatch(args, jobs);
    return;
  }
  await runOne(args, args.model, undefined);
}

main().catch(error => { console.error(`Error: ${error.message}`); process.exitCode = 1; });