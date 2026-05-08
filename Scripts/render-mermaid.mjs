#!/usr/bin/env node
import { readFileSync, writeFileSync } from 'fs';
import { dirname, join } from 'path';
import { fileURLToPath } from 'url';
import { JSDOM } from 'jsdom';
import mermaid from 'mermaid';

const __dirname = dirname(fileURLToPath(import.meta.url));

// Set up DOM globals for mermaid
const dom = new JSDOM('<!DOCTYPE html><html><body></body></html>');
globalThis.document = dom.window.document;
globalThis.window = dom.window;
globalThis.Element = dom.window.Element;
globalThis.SVGElement = dom.window.SVGElement;
globalThis.Node = dom.window.Node;
globalThis.XMLSerializer = dom.window.XMLSerializer;

const { initialize, mermaidAPI } = mermaid;

await initialize({
  startOnLoad: false,
  theme: 'default',
  securityLevel: 'loose',
});

const inputDir = process.argv[2] || join(__dirname, 'docs', 'uml');
const outputDir = process.argv[3] || inputDir;

const files = process.argv.length > 4
  ? process.argv.slice(4)
  : [
      'nars-class-diagram.md',
      'nars-sequence-diagram.md',
      'nars-vite-component-diagram.md',
      'nars-vite-sequence-diagram.md',
    ];

for (const file of files) {
  const filePath = join(inputDir, file);
  console.log(`Processing ${file}...`);

  const content = readFileSync(filePath, 'utf-8');
  const codeBlocks = content.match(/```mermaid\n([\s\S]*?)```/g) || [];

  if (codeBlocks.length === 0) {
    console.log(`  No mermaid blocks found in ${file}`);
    continue;
  }

  for (let i = 0; i < codeBlocks.length; i++) {
    const code = codeBlocks[i].replace(/```mermaid\n/, '').replace(/```$/, '').trim();
    const baseName = file.replace('.md', '');
    const outputName = codeBlocks.length > 1
      ? `${baseName}-${i + 1}.svg`
      : `${baseName}.svg`;
    const outputPath = join(outputDir, outputName);

    console.log(`  Rendering diagram ${i + 1}/${codeBlocks.length}...`);

    try {
      const { svg } = await mermaidAPI.render(`diagram-${i}`, code);
      writeFileSync(outputPath, svg);
      console.log(`  -> ${outputName}`);
    } catch (err) {
      console.error(`  Error rendering diagram ${i + 1}: ${err.message}`);
    }
  }
}

console.log('Done.');
