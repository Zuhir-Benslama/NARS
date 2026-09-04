#!/usr/bin/env node
// Renders ```mermaid blocks from docs/uml/*.md to PNG via headless Firefox.
//
// Run from anywhere: node nars-infra/scripts/render-mermaid-playwright.mjs
// [inputDir] [outputDir] — see CLI parsing below. Exit status is non-zero
// when any requested file is missing, a diagram fails to render, or nothing
// renders at all, so silent breakage (e.g. a moved docs/ directory) fails
// pipelines instead of passing vacuously.
import { existsSync, readFileSync } from 'fs';
import { basename, dirname, join } from 'path';
import { fileURLToPath } from 'url';

const __dirname = dirname(fileURLToPath(import.meta.url));

// playwright is a devDependency of nars-web. ESM bare-specifier resolution
// walks up from THIS file's directory (nars-infra/scripts/) and never reaches
// nars-web/node_modules, so fall back to resolving it through createRequire
// anchored at nars-web's package.json. The plain import() attempt first keeps
// the script working unchanged if a resolvable install ever appears above.
let firefox;
try {
  ({ firefox } = await import('playwright'));
} catch {
  const { createRequire } = await import('node:module');
  const narsWebRequire = createRequire(
    new URL('../../nars-web/package.json', import.meta.url),
  );
  ({ firefox } = narsWebRequire('playwright'));
}

const defaultInputDir = join(__dirname, '..', '..', 'docs', 'uml');
const defaultFiles = [
  'nars-class-diagram.md',
  'nars-sequence-diagram.md',
  'nars-vite-component-diagram.md',
  'nars-vite-sequence-diagram.md',
];

// CLI: node script.mjs [inputDir] [outputDir] [files...]
//   or: node script.mjs <file.md> [outputDir]   (single-file convenience)
const arg1 = process.argv[2];
let inputDir;
let outputDir;
let files;

if (arg1 && arg1.toLowerCase().endsWith('.md')) {
  inputDir = dirname(arg1);
  outputDir = process.argv[3] || inputDir;
  files = [basename(arg1)];
} else {
  inputDir = arg1 || defaultInputDir;
  outputDir = process.argv[3] || inputDir;
  files = process.argv.length > 4 ? process.argv.slice(4) : defaultFiles;
}

async function main() {
  let rendered = 0;
  let failures = 0;

  const browser = await firefox.launch({ headless: true, timeout: 30000 });

  try {
    for (const file of files) {
      const filePath = join(inputDir, file);
      console.log(`Processing ${file}...`);

      if (!existsSync(filePath)) {
        console.error(`  Skipping ${file}: not found (${filePath})`);
        failures++;
        continue;
      }

      const content = readFileSync(filePath, 'utf-8');
      const codeBlocks = content.match(/```mermaid\n([\s\S]*?)```/g) || [];

      if (codeBlocks.length === 0) {
        console.log(`  No mermaid blocks found in ${file}`);
        continue;
      }

      const page = await browser.newPage();

      try {
        for (let i = 0; i < codeBlocks.length; i++) {
          const code = codeBlocks[i].replace(/```mermaid\n/, '').replace(/```$/, '').trim();
          const baseName = file.replace('.md', '');
          const outputName = codeBlocks.length > 1
            ? `${baseName}-${i + 1}.png`
            : `${baseName}.png`;
          const outputPath = join(outputDir, outputName);

          console.log(`  Rendering diagram ${i + 1}/${codeBlocks.length}...`);

          // Try jsdelivr first, fall back to unpkg if unavailable.
          // Pinned to 11.17.2 (not the mutable @11 major tag) so a breaking or
          // vulnerable 11.x release cannot silently change rendered output.
          const mermaidUrl = `https://cdn.jsdelivr.net/npm/mermaid@11.17.2/dist/mermaid.esm.min.mjs`;
          const fallbackUrl = `https://unpkg.com/mermaid@11.17.2/dist/mermaid.esm.min.mjs`;

          const html = `<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8">
  <style>body { margin: 0; padding: 20px; background: white; }</style>
</head>
<body>
  <div id="diagram"></div>
  <script type="module">
    const MERMAID_URL = ${JSON.stringify(mermaidUrl)};
    const FALLBACK_URL = ${JSON.stringify(fallbackUrl)};
    async function loadMermaid() {
      try {
        return await import(MERMAID_URL);
      } catch {
        console.warn('jsdelivr unavailable, trying unpkg fallback');
        return await import(FALLBACK_URL);
      }
    }
    const mermaidModule = await loadMermaid();
    const mermaid = mermaidModule.default || mermaidModule;
    mermaid.initialize({ startOnLoad: false, theme: 'default', fontFamily: 'monospace' });
    const code = ${JSON.stringify(code)};
    // Fail-fast timeout: if mermaid hasn't finished in 12s, report the error.
    const failTimer = setTimeout(() => {
      window.__error = 'Timeout: mermaid rendering exceeded 12s';
      window.__ready = true;
    }, 12000);
    try {
      const { svg } = await mermaid.render('d' + Date.now(), code);
      clearTimeout(failTimer);
      document.getElementById('diagram').innerHTML = svg;
      window.__ready = true;
    } catch (e) {
      clearTimeout(failTimer);
      window.__error = e.message;
      window.__ready = true;
    }
  </script>
</body>
</html>`;

          await page.setViewportSize({ width: 1920, height: 1080 });
          await page.setContent(html, { waitUntil: 'commit' });

          try {
            await page.waitForFunction(() => window.__ready || window.__error, { timeout: 15000 });

            const error = await page.evaluate(() => window.__error);
            if (error) {
              console.error(`  Error: ${error}`);
              failures++;
              continue;
            }

            await new Promise(resolve => setTimeout(resolve, 500));

            const svgElement = await page.locator('#diagram svg');
            const bbox = await svgElement.evaluate(el => {
              const b = el.getBBox();
              return { width: Math.ceil(b.width + 40), height: Math.ceil(b.height + 40) };
            });

            await svgElement.screenshot({ path: outputPath, type: 'png' });
            console.log(`  -> ${outputName} (${bbox.width}x${bbox.height})`);
            rendered++;
          } catch (err) {
            console.error(`  Error rendering: ${err.message}`);
            failures++;
          }
        }
      } finally {
        await page.close();
      }
    }
  } finally {
    await browser.close();
  }

  if (failures > 0) {
    console.error(`Done with ${failures} failure(s); ${rendered} diagram(s) rendered.`);
    process.exitCode = 1;
    return;
  }

  if (rendered === 0) {
    console.error('Done, but no diagrams were rendered — check inputDir/files.');
    process.exitCode = 1;
    return;
  }

  console.log(`Done. ${rendered} diagram(s) rendered.`);
}

main().catch(err => {
  console.error('Fatal error:', err);
  process.exit(1);
});
