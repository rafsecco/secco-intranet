// Tabela do editor: a saida nativa alinha colunas com espacos, gera <br> em celula e cabecalho
// vazio, e escreve um `style` que a CSP em Enforce bloqueia. Aqui a tabela sai no formato que o
// leitor do mural entende: uma linha por linha da tabela, cabecalho na primeira, sem <br>.
import { mergeAttributes } from '@tiptap/core';
import { Table } from '@tiptap/extension-table';

const FOLHAS = new Set(['paragraph', 'heading', 'codeBlock']);

function blocosFolha(no, saida = []) {
  (no.content || []).forEach((filho) => {
    if (FOLHAS.has(filho.type)) {
      saida.push(filho);
    } else if (filho.content) {
      blocosFolha(filho, saida);
    }
  });
  return saida;
}

function celula(c, h) {
  return blocosFolha(c)
    .map((b) => (b.content ? h.renderChildren(b.content) : ''))
    .join(' ')
    .replace(/\s*\r?\n\s*/g, ' ')
    .replace(/<br\s*\/?>/gi, ' ')
    .replace(/\\.|\|/g, (m) => (m === '|' ? '\\|' : m))
    .replace(/\s+/g, ' ')
    .trim();
}

export const Tabela = Table.extend({
  renderHTML({ HTMLAttributes }) {
    return ['table', mergeAttributes(this.options.HTMLAttributes, HTMLAttributes), ['tbody', 0]];
  },
  renderMarkdown(node, h) {
    const linhas = (node.content || []).map((l) => (l.content || []).map((c) => celula(c, h)));
    const n = Math.max(0, ...linhas.map((l) => l.length));
    if (!n) {
      return '';
    }
    const fmt = (l) => `| ${Array.from({ length: n }, (_, i) => l[i] ?? '').join(' | ')} |`;
    return ['', fmt(linhas[0]), `| ${Array(n).fill('---').join(' | ')} |`, ...linhas.slice(1).map(fmt), ''].join('\n');
  },
});
