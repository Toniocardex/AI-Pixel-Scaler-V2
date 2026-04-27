// SVG → PNG 256 + app.ico (npm i in scripts/: sharp png-to-ico)
import { readFileSync, writeFileSync } from "fs";
import { dirname, join } from "path";
import { fileURLToPath } from "url";

const __dir = dirname(fileURLToPath(import.meta.url));
const root = join(__dir, "..");
const assets = join(root, "src", "AiPixelScaler.Desktop", "Assets");
const svgPath = join(assets, "app-icon.svg");
const outPng = join(assets, "app-icon-256.png");
const outIco = join(assets, "app.ico");

async function main() {
  const { default: sharp } = await import("sharp");
  const toIco = (await import("to-ico")).default;
  const svg = readFileSync(svgPath);
  const mk = (s) => sharp(svg).resize(s, s).png().toBuffer();
  const [b16, b32, b48, b64, b256] = await Promise.all([
    mk(16), mk(32), mk(48), mk(64), mk(256),
  ]);
  writeFileSync(outPng, b256);
  const ico = await toIco([b16, b32, b48, b64, b256]);
  writeFileSync(outIco, ico);
  console.log("OK", outPng, outIco);
}
main().catch((e) => {
  console.error(e);
  process.exit(1);
});
