#!/usr/bin/env node
import { spawn } from "node:child_process";
import { access, stat } from "node:fs/promises";
import { dirname, resolve } from "node:path";

const transient = /408|409|429|500|502|503|504|timeout|temporar|connection|unavailable|server error/i;
const configuredModel = process.env.OPENAI_IMAGE_MODEL;
const models = [...new Set([configuredModel, "gpt-image-2", "gpt-image-1.5", "gpt-image-1"].filter(Boolean))];
const script = resolve(dirname(new URL(import.meta.url).pathname), "image_gen.mjs");
const args = process.argv.slice(2);
const explicitlySelected = args.includes("--model") ? args[args.indexOf("--model") + 1] : undefined;
const candidates = explicitlySelected ? [explicitlySelected] : models;
const outputIndex = args.indexOf("--out");
const output = outputIndex >= 0 ? args[outputIndex + 1] : undefined;

function run(commandArgs) {
  return new Promise(resolveRun => {
    const child = spawn(process.execPath, [script, ...commandArgs], { stdio: ["inherit", "pipe", "pipe"], env: process.env });
    let stdout = ""; let stderr = "";
    child.stdout.on("data", chunk => { stdout += chunk; });
    child.stderr.on("data", chunk => { stderr += chunk; });
    child.on("close", code => resolveRun({ code: code ?? 1, stdout, stderr }));
  });
}

for (const [index, model] of candidates.entries()) {
  for (let attempt = 1; attempt <= 3; attempt += 1) {
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
    if (attempt < 3) await new Promise(resolveWait => setTimeout(resolveWait, Math.min(8000, 2 ** attempt * 1000)));
  }
  if (index < candidates.length - 1) process.stderr.write(`${model} unavailable; trying ${candidates[index + 1]}\n`);
}
process.stderr.write("All configured GPT Image models failed after retries.\n");
process.exit(1);