// Entrada do bundle do editor do mural. O esbuild empacota o Tiptap num IIFE autocontido e expoe
// o global `seccoEditor`, que wwwroot/js/editor.js usa. O resultado e versionado em
// wwwroot/vendor/js/editor-tiptap.js: `dotnet run` precisa funcionar sem Node instalado.
import { Editor } from '@tiptap/core';
import { Markdown } from '@tiptap/markdown';
import { TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import StarterKit from '@tiptap/starter-kit';
import { Tabela } from './tabela-markdown.js';

const ESQUEMAS = new Set(['http:', 'https:', 'mailto:', 'tel:']);

// Aceita caminho sem esquema e os esquemas http, https, mailto e tel. O resto (javascript:, data:)
// nao vira link.
function destinoPermitido(url) {
  const texto = String(url ?? '').trim();
  const esquema = /^([a-z][a-z0-9+.-]*:)/i.exec(texto);

  return !esquema || ESQUEMAS.has(esquema[1].toLowerCase());
}

export function criarEditor({ elemento, markdown, aoMudar }) {
  return new Editor({
    element: elemento,
    // A <style> injetada violaria a CSP; o CSS vem do tema.
    injectCSS: false,
    content: markdown || '',
    contentType: 'markdown',
    extensions: [
      StarterKit.configure({
        underline: false,
        strike: false,
        link: { autolink: false, linkOnPaste: false, openOnClick: false, isAllowedUri: destinoPermitido },
      }),
      Tabela.configure({ resizable: false }),
      TableRow,
      TableHeader,
      TableCell,
      Markdown,
    ],
    editorProps: { attributes: { role: 'textbox', 'aria-multiline': 'true', 'aria-label': 'Corpo' } },
    onUpdate: ({ editor }) => aoMudar(editor.getMarkdown()),
  });
}
