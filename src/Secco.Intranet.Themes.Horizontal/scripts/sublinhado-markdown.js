// Sublinhado do editor. O Markdown nao tem sublinhado; a sintaxe do mural e `++texto++`, que o
// servidor (Markdig, EmphasisExtras.Inserted) renderiza como <ins>. A extensao nativa grava
// `<u>texto</u>`, que o leitor do mural nao entende, entao a escrita e a leitura do Markdown
// trocam por `++`. O resto (tecla Ctrl+U, comandos, <u> no HTML colado) e o da extensao nativa.
import { Underline } from '@tiptap/extension-underline';

// Abre com `++` e fecha com `++`, sem espaco colado aos delimitadores e sem `+` no miolo.
const SUBLINHADO = /^\+\+([^\s+](?:[^+\n]*[^\s+])?)\+\+/;

export const Sublinhado = Underline.extend({
  parseMarkdown(token, helpers) {
    return helpers.applyMark(this.name || 'underline', helpers.parseInline(token.tokens || []));
  },

  renderMarkdown(node, helpers) {
    return `++${helpers.renderChildren(node)}++`;
  },

  markdownTokenizer: {
    name: 'underline',
    level: 'inline',
    start: (src) => src.indexOf('++'),
    tokenize(src, _tokens, lexer) {
      const achado = SUBLINHADO.exec(src);

      if (!achado) {
        return undefined;
      }

      return {
        type: 'underline',
        raw: achado[0],
        text: achado[1],
        tokens: lexer.inlineTokens(achado[1]),
      };
    },
  },
});
