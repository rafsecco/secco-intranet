// Copia os assets de terceiros para wwwroot/vendor. Rodado por `npm run copy:assets`.
// O resultado e versionado: `dotnet run` precisa funcionar sem Node instalado.
import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const from = (...parts) => join(root, 'node_modules', ...parts);
const to = (...parts) => join(root, 'wwwroot', 'vendor', ...parts);

const fonts = [
  ['@fontsource/instrument-sans/files/instrument-sans-latin-600-normal.woff2'],
  ['@fontsource/instrument-sans/files/instrument-sans-latin-700-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-400-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-500-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-600-normal.woff2'],
  ['@fontsource/jetbrains-mono/files/jetbrains-mono-latin-400-normal.woff2'],
  ['@fontsource/jetbrains-mono/files/jetbrains-mono-latin-500-normal.woff2'],
  ['bootstrap-icons/font/fonts/bootstrap-icons.woff2'],
];

await mkdir(to('fonts'), { recursive: true });
await mkdir(to('js'), { recursive: true });

for (const [source] of fonts) {
  const name = source.split('/').pop();
  await copyFile(from(source), to('fonts', name));
  console.log(`fonts/${name}`);
}

await copyFile(from('bootstrap/dist/js/bootstrap.bundle.min.js'), to('js', 'bootstrap.bundle.min.js'));
console.log('js/bootstrap.bundle.min.js');
