#!/usr/bin/env node
import { spawn } from "node:child_process";
import { access, stat } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const transient = /408|409|429|500|502|503|504|timeout|temporar|connection|unavailable|server error/i;
const configuredModel = process.env.OPENAI_IMAGE_MODEL;
const models = [...new Set([configuredModel, "gpt-image-2", "gpt-image-1.5", "gpt-image-1"].filter(Boolean))];
const script = resolve(dirname(fileURLToPath(import.meta.url)), "image_gen.mjs");
const args = process.argv.slice(2);
const explicitlySelected = args.includes("--model") ? args[args.indexOf("--model") + 1] : undefined;
const candidates = explicitlySelected
  ? [explicitlySelected]
  : configuredModel?.startsWith("gemini-")
    ? [configuredModel]
    : models;
const outputIndex = args.indexOf("--out");
const output = outputIndex >= 0 ? args[outputIndex + 1] : undefined;

function run(commandArgs) {
  return new Promise(resolveRun => {
    const model = commandArgs[commandArgs.indexOf("--model") + 1];
    process.stderr.write(`[imagegen] ${model}: starting image generation request\n`);
    const child = spawn(process.execPath, [script, ...commandArgs], { stdio: ["inherit", "pipe", "pipe"], env: process.env });
    let stdout = ""; let stderr = "";
    child.stdout.on("data", chunk => { stdout += chunk; });
    child.stderr.on("data", chunk => { stderr += chunk; });
    const waiting = setInterval(() => process.stderr.write(`[imagegen] ${model}: provider is still rendering; waiting for the image response\n`), 15000);
    child.on("error", error => {
      clearInterval(waiting);
      resolveRun({ code: 1, stdout, stderr: `${stderr}\n${error.message}` });
    });
    child.on("close", code => {
      clearInterval(waiting);
      resolveRun({ code: code ?? 1, stdout, stderr });
    });
  });
}

for (const [index, model] of candidates.entries()) {
  for (let attempt = 1; attempt <= 3; attempt += 1) {
    process.stderr.write(`[imagegen] ${model}: attempt ${attempt}/3\n`);
    const forwardedArgs = [];
    for (let i = 1; i < args.length; i += 1) {
      if (args[i] === "--model") {
        i += 1;
        continue;
      }
      forwardedArgs.push(args[i]);
    }
    const commandArgs = [args[0], "--model", model, ...forwardedArgs];
    const result = await run(commandArgs);
    const outputReady = !output || (await access(output).then(() => stat(output)).then(info => info.size > 0).catch(() => false));
    if (result.code === 0 && (args.includes("--dry-run") || outputReady)) { process.stdout.write(result.stdout); process.stderr.write(result.stderr); process.exit(0); }
    const details = `${result.stderr}\n${result.stdout}`;
    if (explicitlySelected || !transient.test(details)) { process.stderr.write(result.stderr || result.stdout); process.exit(result.code || 1); }
    if (attempt < 3) {
      const delay = Math.min(8000, 2 ** attempt * 1000);
      process.stderr.write(`[imagegen] ${model}: transient failure; retrying in ${delay / 1000}s\n`);
      await new Promise(resolveWait => setTimeout(resolveWait, delay));
    }
  }
  if (index < candidates.length - 1) process.stderr.write(`[imagegen] ${model} unavailable; trying ${candidates[index + 1]}\n`);
}
process.stderr.write("All configured GPT Image models failed after retries.\n");
process.exit(1);