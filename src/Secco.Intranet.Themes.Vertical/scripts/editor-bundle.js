// Entrada do bundle do editor do mural. O esbuild empacota o Tiptap num IIFE autocontido e expoe
// o global `seccoEditor`, que wwwroot/js/editor.js usa. O resultado e versionado em
// wwwroot/vendor/js/editor-tiptap.js: `dotnet run` precisa funcionar sem Node instalado.
import { Editor } from '@tiptap/core';
import { Markdown } from '@tiptap/markdown';
import { TableCell, TableHeader, TableRow } from '@tiptap/extension-table';
import StarterKit from '@tiptap/starter-kit';
import { Tokenizer } from 'marked';
import { Tabela } from './tabela-markdown.js';
import { limparColagem } from './colagem.js';
import { htmlSemFormatacao, pareceMarkdown } from './markdown-colado.js';

export { htmlSemFormatacao, pareceMarkdown } from './markdown-colado.js';
export { limparColagem, AVISO_ANINHADA, AVISO_IMAGEM, AVISO_MESCLADA, AVISO_TABELA_GRANDE } from './colagem.js';

const ESQUEMAS = new Set(['http:', 'https:', 'mailto:', 'tel:']);

// Aceita caminho sem esquema e os esquemas http, https, mailto e tel. O resto (javascript:, data:)
// nao vira link.
function destinoPermitido(url) {
  const texto = String(url ?? '').trim();
  const esquema = /^([a-z][a-z0-9+.-]*:)/i.exec(texto);

  return !esquema || ESQUEMAS.has(esquema[1].toLowerCase());
}

// No Markdown padrao (e no GitHub) a quebra simples dentro do paragrafo e so um espaco; o `marked`
// a deixa como "\n" no texto, que o editor mostraria como quebra de linha. So a quebra forcada
// (dois espacos e "\n", ou Shift+Enter) e quebra: ela chega como token `br`, nao como texto.
class TokenizadorSemQuebraSimples extends Tokenizer {
  inlineText(src) {
    const token = super.inlineText(src);

    if (token) {
      token.text = token.text.replace(/\n/g, ' ');
    }

    return token;
  }
}

export function criarEditor({ elemento, markdown, aoMudar, aoColar }) {
  const editor = new Editor({
    element: elemento,
    // A <style> injetada violaria a CSP; o CSS vem do tema.
    injectCSS: false,
    content: markdown || '',
    contentType: 'markdown',
    extensions: [
      StarterKit.configure({
        underline: false,
        strike: false,
        link: {
          autolink: false,
          linkOnPaste: false,
          openOnClick: false,
          isAllowedUri: destinoPermitido,
          // Texto colado so vira link com esquema explicito: "ASP.NET" nao e um dominio.
          shouldAutoLink: (url) => /^[a-z][a-z0-9+.-]*:\/\//i.test(url),
        },
      }),
      Tabela.configure({ resizable: false }),
      TableRow,
      TableHeader,
      TableCell,
      Markdown.configure({ markedOptions: { tokenizer: new TokenizadorSemQuebraSimples() } }),
    ],
    editorProps: {
      attributes: { role: 'textbox', 'aria-multiline': 'true', 'aria-label': 'Corpo' },
      // Colar texto que pareca Markdown vira formatacao quando nao ha text/html ou quando ele nao
      // tem formatacao (o VS Code manda so div/span/br). Com text/html de verdade, a colagem segue
      // pelo transformPastedHTML (Word, sites).
      handlePaste: (view, event) => {
        const dados = event.clipboardData;

        if (!dados) {
          return false;
        }

        const html = (dados.getData('text/html') || '').trim();

        if (html !== '' && !htmlSemFormatacao(html)) {
          return false;
        }

        const texto = dados.getData('text/plain') || '';

        if (!pareceMarkdown(texto)) {
          return false;
        }

        // `editor` ainda nao existe na primeira chamada, mas o handler so roda depois do construtor.
        editor.commands.insertContent(texto, { contentType: 'markdown' });

        return true;
      },
      transformPastedHTML: (html) => {
        const limpo = limparColagem(html);
        aoColar?.(limpo.avisos);

        return limpo.html;
      },
    },
    onUpdate: ({ editor }) => aoMudar(editor.getMarkdown()),
  });

  return editor;
}
