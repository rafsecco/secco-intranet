// Copia os assets de terceiros para wwwroot/vendor. Rodado por `npm run copy:assets`.
// O resultado e versionado: `dotnet run` precisa funcionar sem Node instalado.
import { copyFile, mkdir } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = join(dirname(fileURLToPath(import.meta.url)), '..');
const from = (...parts) => join(root, 'node_modules', ...parts);
const to = (...parts) => join(root, 'wwwroot', 'vendor', ...parts);

// So Inter e JetBrains Mono: o Horizontal usa uma familia so (por peso, nao por par de
// tipografias) em vez do par Instrument Sans + Inter do Vertical (ver _variables.scss).
const fonts = [
  ['@fontsource/inter/files/inter-latin-400-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-500-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-600-normal.woff2'],
  ['@fontsource/inter/files/inter-latin-700-normal.woff2'],
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

// O editor nao entra aqui: o JS vem de `npm run build:js` (bundle Tiptap autocontido) e o CSS vive
// no Sass do tema.
