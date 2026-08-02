import { access, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { dirname, join } from "node:path";

const root = dirname(dirname(fileURLToPath(import.meta.url)));
const required = [".codex-plugin/plugin.json", ".mcp.json", "package.json", "scripts/image-mcp-server.mjs", "skills/product-listing-image/SKILL.md"];
let errors = [];
if (Number(process.versions.node.split(".")[0]) < 20) errors.push("Node.js 20 or newer is required.");
for (const relative of required) { try { await access(join(root, relative)); } catch { errors.push(`Missing required file: ${relative}`); } }
for (const relative of [".codex-plugin/plugin.json", ".mcp.json", "package.json"]) { try { JSON.parse(await readFile(join(root, relative), "utf8")); } catch { errors.push(`Invalid JSON: ${relative}`); } }
const manifest = JSON.parse(await readFile(join(root, ".codex-plugin/plugin.json"), "utf8"));
const mcp = JSON.parse(await readFile(join(root, ".mcp.json"), "utf8"));
if (manifest.name !== "ecommerce-image-studio") errors.push("plugin.json name must match the plugin folder.");
if (!mcp.mcpServers?.["ecommerce-image-studio"]) errors.push("Missing ecommerce-image-studio MCP server configuration.");
if (errors.length) { console.error(errors.join("\n")); process.exitCode = 1; } else console.log("Plugin validation passed.");
