// Imagem no meio do texto: o no `image` do Tiptap, que grava marcador ou caminho no Markdown e mostra
// a pre-visualizacao pela URL de imagens-novas.js, e o no `imagemPendente`, o lugar de uma imagem
// que o editor nao consegue mostrar (marcador sem arquivo, endereco de fora, outra publicacao).
import { Node, mergeAttributes } from '@tiptap/core';
import Image from '@tiptap/extension-image';
import { LIMITE_IMAGENS, registrar, urlDe } from './imagens-novas.js';

const ESQUEMAS_DE_IMAGEM = new Set(['nova-imagem', 'pendente', 'http', 'https']);

export const LIMITE_BYTES = 10 * 1024 * 1024;
export const TIPOS_DE_IMAGEM = ['image/png', 'image/jpeg', 'image/webp'];
export const AVISO_IMAGEM_GRANDE = 'A imagem passa de 10 MB.';
export const AVISO_IMAGEM_TIPO = 'Use PNG, JPG ou WebP.';
export const AVISO_IMAGENS_DEMAIS = 'O texto já tem 10 imagens.';
export const TEXTO_PENDENTE = 'Esta imagem não veio. Clique para enviar o arquivo.';
export const MARKDOWN_PENDENTE = '![imagem pendente](pendente:)';

// O `]` e a `\` da descricao fecham ou escapam o Markdown: vao escapados, e a quebra vira espaco.
function descricaoParaMarkdown(texto) {
  return String(texto ?? '').replace(/\r?\n/g, ' ').replace(/([\\[\]])/g, '\\$1');
}

export const ImagemPendente = Node.create({
  name: 'imagemPendente',
  group: 'inline',
  inline: true,
  atom: true,
  selectable: true,

  parseHTML() {
    return [{ tag: 'button[data-imagem-pendente]' }];
  },

  renderHTML() {
    return ['button', {
      type: 'button',
      class: 'btn btn-sm btn-outline-warning sc-editor__pendente',
      'data-imagem-pendente': '',
    }, TEXTO_PENDENTE];
  },

  renderMarkdown() {
    return MARKDOWN_PENDENTE;
  },
});

export const ImagemDoTexto = Image.extend({
  addAttributes() {
    return {
      ...this.parent?.(),
      // A <img> mostra a URL da pre-visualizacao; o marcador original volta de data-marcador.
      src: {
        default: null,
        parseHTML: (elemento) => elemento.getAttribute('data-marcador') || elemento.getAttribute('src'),
        renderHTML: (atributos) => (atributos.src ? { src: atributos.src } : {}),
      },
    };
  },

  renderHTML({ HTMLAttributes }) {
    const { src, ...resto } = HTMLAttributes;
    const url = urlDe(src);
    const exibicao = url ? { src: url, 'data-marcador': src } : {};

    return ['img', mergeAttributes(this.options.HTMLAttributes, resto, exibicao)];
  },

  // Endereco que o editor nao sabe mostrar nunca vira <img>: vira o botao de reenvio.
  // Esquema que nao e imagem de verdade (javascript:, data:, file:) nao vira nem botao: sobra o
  // texto alternativo, ou nada.
  parseMarkdown(token, helpers) {
    if (!urlDe(token.href)) {
      const esquema = /^([a-z][a-z0-9+.-]*):/i.exec(String(token.href ?? '').trim());

      if (esquema && !ESQUEMAS_DE_IMAGEM.has(esquema[1].toLowerCase())) {
        return token.text ? helpers.createTextNode(token.text) : [];
      }

      return helpers.createNode('imagemPendente');
    }

    return helpers.createNode('image', { src: token.href, alt: token.text });
  },

  renderMarkdown(node) {
    return `![${descricaoParaMarkdown(node.attrs?.alt)}](${node.attrs?.src ?? ''})`;
  },
});

// O aviso do motivo de o arquivo nao poder virar imagem do texto, ou null se pode. `ocupadas` e
// quantas imagens (e pendentes) o texto ja tem.
export function validarImagem(arquivo, ocupadas) {
  if (!TIPOS_DE_IMAGEM.includes(arquivo.type)) {
    return AVISO_IMAGEM_TIPO;
  }

  if (arquivo.size > LIMITE_BYTES) {
    return AVISO_IMAGEM_GRANDE;
  }

  return ocupadas >= LIMITE_IMAGENS ? AVISO_IMAGENS_DEMAIS : null;
}

export function contarImagens(documento) {
  let total = 0;

  documento.descendants((no) => {
    if (no.type.name === 'image' || no.type.name === 'imagemPendente') {
      total += 1;
    }
  });

  return total;
}

// Valida e insere os arquivos como imagens novas; devolve os avisos do que ficou de fora.
// `substituir` e a posicao de um `imagemPendente` que o primeiro arquivo aceito troca; `posicao`, onde
// inserir (arrasto); sem nenhum dos dois, na selecao.
export function inserirArquivos(editor, arquivos, { posicao, substituir } = {}) {
  const avisos = new Set();
  const nos = [];
  const ocupadas = contarImagens(editor.state.doc) - (substituir === undefined ? 0 : 1);

  Array.from(arquivos).forEach((arquivo) => {
    const recusa = validarImagem(arquivo, ocupadas + nos.length);

    if (recusa) {
      avisos.add(recusa);
    } else {
      nos.push({ type: 'image', attrs: { src: registrar(arquivo), alt: '' } });
    }
  });

  if (nos.length > 0) {
    if (substituir !== undefined) {
      editor.chain().focus().insertContentAt({ from: substituir, to: substituir + 1 }, nos).run();
    } else if (posicao !== undefined) {
      editor.chain().focus().insertContentAt(posicao, nos).run();
    } else {
      editor.chain().focus().insertContent(nos).run();
    }
  }

  return [...avisos];
}

export function arquivosDeImagem(dados) {
  return Array.from(dados?.files ?? []).filter((arquivo) => arquivo.type.startsWith('image/'));
}
