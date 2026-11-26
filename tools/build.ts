import { copyFile, readFile } from "node:fs/promises";
import { fileURLToPath } from "node:url";
import { build } from "esbuild";

const root = fileURLToPath(new URL("..", import.meta.url));
const output = new URL("../web/dist/", import.meta.url);
const licenses = await Promise.all(
  ["gifuct-js", "js-binary-schema-parser"].map(async (name) => {
    const license = await readFile(new URL(`../node_modules/${name}/LICENSE`, import.meta.url), "utf8");
    return `/*! ${name}\n${license.trim()}\n*/`;
  })
);

await build({
  absWorkingDir: root,
  entryPoints: {
    app: "web/src/main.ts",
  },
  outdir: fileURLToPath(output),
  bundle: true,
  minify: true,
  legalComments: "inline",
  banner: { js: licenses.join("\n") },
  format: "iife",
  target: "es2022",
  charset: "utf8",
});

await copyFile(new URL("../web/index.html", import.meta.url), new URL("index.html", output));
