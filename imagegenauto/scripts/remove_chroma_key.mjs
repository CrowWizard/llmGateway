#!/usr/bin/env node
import { access } from "node:fs/promises";
import { dirname, extname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const scriptsDirectory = dirname(fileURLToPath(import.meta.url));
const runtimeModule = resolve(scriptsDirectory, "..", "..", "noderuntime", "scripts", "runtime.mjs");
const { importSharedPackage } = await import(runtimeModule);
const sharp = await importSharedPackage("sharp");

function die(message) { throw new Error(message); }

function parseArgs(argv) {
  const args = { key_color: "#00ff00", tolerance: 12, auto_key: "none", transparent_threshold: 12, opaque_threshold: 96, edge_feather: 0, edge_contract: 0 };
  for (let index = 0; index < argv.length; index += 1) {
    const token = argv[index];
    if (["--soft-matte", "--spill-cleanup", "--despill", "--force"].includes(token)) {
      args[token === "--despill" ? "spill_cleanup" : token.slice(2).replaceAll("-", "_")] = true;
      continue;
    }
    if (!token.startsWith("--") || index + 1 >= argv.length) die(`Invalid argument: ${token}`);
    const key = token.slice(2).replaceAll("-", "_");
    const value = argv[++index];
    args[key] = ["tolerance", "transparent_threshold", "opaque_threshold", "edge_feather", "edge_contract"].includes(key) ? Number(value) : value;
  }
  return args;
}

function parseColor(value) {
  const match = /^#?([0-9a-f]{6})$/i.exec(value);
  if (!match) die("--key-color must be a hex RGB value like #00ff00.");
  return [0, 2, 4].map(offset => Number.parseInt(match[1].slice(offset, offset + 2), 16));
}

async function validate(args) {
  if (!args.input || !args.out) die("--input and --out are required.");
  for (const [name, value, maximum] of [["tolerance", args.tolerance, 255], ["transparent-threshold", args.transparent_threshold, 255], ["opaque-threshold", args.opaque_threshold, 255], ["edge-feather", args.edge_feather, 64], ["edge-contract", args.edge_contract, 16]]) {
    if (!Number.isFinite(value) || value < 0 || value > maximum) die(`--${name} must be between 0 and ${maximum}.`);
  }
  if (args.soft_matte && args.transparent_threshold >= args.opaque_threshold) die("--transparent-threshold must be lower than --opaque-threshold.");
  if (!Number.isInteger(args.edge_contract)) die("--edge-contract must be an integer.");
  if (!new Set(["none", "corners", "border"]).has(args.auto_key)) die("--auto-key must be none, corners, or border.");
  if (!new Set([".png", ".webp"]).has(extname(args.out).toLowerCase())) die("--out must end in .png or .webp so the alpha channel is preserved.");
  if (!await access(args.input).then(() => true, () => false)) die(`Input image not found: ${args.input}`);
  if (!args.force && await access(args.out).then(() => true, () => false)) die(`Output already exists: ${args.out} (use --force to overwrite)`);
}

function median(values) {
  values.sort((left, right) => left - right);
  const middle = Math.floor(values.length / 2);
  return values.length % 2 ? values[middle] : Math.round((values[middle - 1] + values[middle]) / 2);
}

function sampleKey(data, width, height, mode) {
  const samples = [];
  const add = (x, y) => { const offset = (y * width + x) * 4; samples.push([data[offset], data[offset + 1], data[offset + 2]]); };
  if (mode === "corners") {
    const patch = Math.max(1, Math.min(width, height, 12));
    for (const [left, top] of [[0, 0], [width - patch, 0], [0, height - patch], [width - patch, height - patch]]) {
      for (let y = top; y < top + patch; y += 1) for (let x = left; x < left + patch; x += 1) add(x, y);
    }
  } else {
    const band = Math.max(1, Math.min(width, height, 6));
    const step = Math.max(1, Math.floor(Math.min(width, height) / 256));
    for (let x = 0; x < width; x += step) for (let y = 0; y < band; y += 1) { add(x, y); add(x, height - 1 - y); }
    for (let y = 0; y < height; y += step) for (let x = 0; x < band; x += 1) { add(x, y); add(width - 1 - x, y); }
  }
  return [0, 1, 2].map(channel => median(samples.map(sample => sample[channel])));
}

function spillChannels(key) {
  const maximum = Math.max(...key);
  return maximum < 128 ? [] : key.map((value, index) => value >= maximum - 16 && value >= 128 ? index : -1).filter(index => index >= 0);
}

function smoothstep(value) {
  const bounded = Math.max(0, Math.min(1, value));
  return bounded * bounded * (3 - 2 * bounded);
}

function applyMatte(data, key, args) {
  const spill = spillChannels(key);
  let transparent = 0;
  for (let offset = 0; offset < data.length; offset += 4) {
    const rgb = [data[offset], data[offset + 1], data[offset + 2]];
    const distance = Math.max(...rgb.map((value, channel) => Math.abs(value - key[channel])));
    const otherChannels = [0, 1, 2].filter(channel => !spill.includes(channel));
    const dominance = spill.length ? Math.min(...spill.map(channel => rgb[channel])) - Math.max(0, ...otherChannels.map(channel => rgb[channel])) : 0;
    const keyLike = distance <= 32 || !spill.length || dominance >= 16;
    let alpha = distance <= args.tolerance ? 0 : 255;
    if (args.soft_matte && keyLike) {
      const distanceAlpha = distance <= args.transparent_threshold ? 0 : distance >= args.opaque_threshold ? 255 : Math.round(255 * smoothstep((distance - args.transparent_threshold) / (args.opaque_threshold - args.transparent_threshold)));
      const denominator = Math.max(1, Math.max(...key) - Math.max(0, ...otherChannels.map(channel => rgb[channel])));
      const dominanceAlpha = dominance > 0 ? Math.round(255 * (1 - Math.min(1, dominance / denominator))) : 255;
      alpha = Math.min(distanceAlpha, dominanceAlpha);
    }
    alpha = Math.round(alpha * data[offset + 3] / 255);
    if (alpha <= 8) alpha = 0;
    data[offset + 3] = alpha;
    if (alpha === 0) { data[offset] = 0; data[offset + 1] = 0; data[offset + 2] = 0; transparent += 1; }
    else if (args.spill_cleanup && keyLike && alpha < 252 && spill.length) {
      const cap = Math.max(0, ...otherChannels.map(channel => rgb[channel])) - 1;
      for (const channel of spill) data[offset + channel] = Math.max(0, Math.min(data[offset + channel], cap));
    }
  }
  return transparent;
}

function filterAlpha(data, width, height, radius, mode) {
  if (!radius) return;
  const source = Buffer.from(data.filter((_, index) => index % 4 === 3));
  const distance = Math.max(1, Math.ceil(radius));
  for (let y = 0; y < height; y += 1) for (let x = 0; x < width; x += 1) {
    let value = mode === "min" ? 255 : 0;
    let count = 0;
    for (let sampleY = Math.max(0, y - distance); sampleY <= Math.min(height - 1, y + distance); sampleY += 1) for (let sampleX = Math.max(0, x - distance); sampleX <= Math.min(width - 1, x + distance); sampleX += 1) {
      const sample = source[sampleY * width + sampleX];
      if (mode === "min") value = Math.min(value, sample); else { value += sample; count += 1; }
    }
    data[(y * width + x) * 4 + 3] = mode === "min" ? value : Math.round(value / count);
  }
}

async function main() {
  const args = parseArgs(process.argv.slice(2));
  await validate(args);
  const decoded = await sharp(args.input).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const { width, height } = decoded.info;
  const key = args.auto_key === "none" ? parseColor(args.key_color) : sampleKey(decoded.data, width, height, args.auto_key);
  const transparentBeforeEdges = applyMatte(decoded.data, key, args);
  for (let iteration = 0; iteration < args.edge_contract; iteration += 1) filterAlpha(decoded.data, width, height, 1, "min");
  filterAlpha(decoded.data, width, height, args.edge_feather, "average");
  let transparent = 0;
  let partial = 0;
  for (let offset = 3; offset < decoded.data.length; offset += 4) { if (decoded.data[offset] === 0) transparent += 1; else if (decoded.data[offset] < 255) partial += 1; }
  const output = sharp(decoded.data, { raw: { width, height, channels: 4 } });
  await (extname(args.out).toLowerCase() === ".webp" ? output.webp() : output.png()).toFile(args.out);
  console.log(`Wrote ${args.out}`);
  console.log(`Key color: #${key.map(value => value.toString(16).padStart(2, "0")).join("")}`);
  console.log(`Transparent pixels: ${transparent}/${width * height}`);
  console.log(`Partially transparent pixels: ${partial}/${width * height}`);
  if (transparentBeforeEdges === 0) console.error("Warning: no pixels matched the key color before feathering.");
}

main().catch(error => { console.error(`Error: ${error.message}`); process.exitCode = 1; });