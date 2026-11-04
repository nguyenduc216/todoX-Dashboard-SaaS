// Builds the Remotion bundle that ships with todox-editvideo.exe: `npm run bundle` -> ./build
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { bundle } from '@remotion/bundler';

const root = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const out = await bundle({
  entryPoint: path.join(root, 'src', 'index.ts'),
  outDir: path.join(root, 'build'),
});
console.log(`bundle: ${out}`);
