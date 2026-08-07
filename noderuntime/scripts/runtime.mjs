import { access } from "node:fs/promises";
import { createRequire } from "node:module";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const skillRoot = resolve(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(import.meta.url);

export function runtimeIdentifier() { return "win-x64"; }

export function managedNodePath() {
  return join(skillRoot, "runtime", runtimeIdentifier(), "node.exe");
}

export async function hasManagedNode() {
  return access(managedNodePath()).then(() => true, () => false);
}

export async function importSharedPackage(name) {
  return require(name);
}