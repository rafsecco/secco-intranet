// Limpeza do HTML colado no editor do mural (Word, Excel, paginas da web). A entrada e lida num
// documento inerte (DOMParser: nada executa, nada carrega) e a saida e o corpo desse documento,
// ja sem estilo, imagem, paragrafo vazio, e com as tabelas no formato que o markdown do mural
// suporta: grade retangular, sem celula mesclada, primeira linha como cabecalho, uma linha de
// texto por celula.

export const AVISO_MESCLADA = 'Esta tabela tinha células mescladas, que não são suportadas. Confira o resultado ou envie a tabela como imagem.';
export const AVISO_ANINHADA = 'Esta tabela tinha outra tabela dentro dela, o que não é suportado. Confira o resultado ou envie a tabela como imagem.';
export const AVISO_IMAGEM = 'A imagem não foi colada. Use o campo Imagem.';

// Teto de mesclagem: um colspan="100000" vindo de um HTML malicioso nao pode alocar a grade.
const LIMITE_MESCLAGEM = 100;
const REMOVIDOS = 'script, style, meta, link, title, template, iframe, object, embed';
const MARCAS_EM_LINHA = new Set(['B', 'STRONG', 'I', 'EM', 'A', 'CODE', 'SPAN', 'U', 'S', 'SUB', 'SUP', 'MARK']);

function vazio(texto) {
  return texto.replace(/[\s\xa0]/g, '') === '';
}

function desembrulhar(no) {
  no.replaceWith(...no.childNodes);
}

function mescla(valor) {
  const n = Number.parseInt(valor, 10);

  return Number.isFinite(n) ? Math.min(Math.max(n, 1), LIMITE_MESCLAGEM) : 1;
}

// Copia o conteudo de `origem` para `destino` como texto em linha: quebras, paragrafos, itens de
// lista e titulos viram espaco (o titulo, alem disso, vira negrito). So as marcas em linha ficam.
function emLinha(origem, destino, doc) {
  origem.childNodes.forEach((no) => {
    if (no.nodeType === 3) {
      destino.appendChild(doc.createTextNode(no.nodeValue));
    } else if (no.nodeType !== 1) {
      return;
    } else if (no.tagName === 'BR') {
      destino.appendChild(doc.createTextNode(' '));
    } else if (/^H[1-6]$/.test(no.tagName)) {
      const negrito = doc.createElement('strong');
      emLinha(no, negrito, doc);
      destino.appendChild(negrito);
      destino.appendChild(doc.createTextNode(' '));
    } else if (MARCAS_EM_LINHA.has(no.tagName)) {
      const copia = no.cloneNode(false);
      emLinha(no, copia, doc);
      destino.appendChild(copia);
    } else {
      emLinha(no, destino, doc);
      destino.appendChild(doc.createTextNode(' '));
    }
  });
}

// O DOMParser herda a CSP da pagina: um `style="..."` no HTML colado, ao ser lido, registraria
// violacao (style-src-attr). Entao o atributo sai ainda no texto, antes de ler. Negrito e italico
// escritos so como CSS (Google Docs) viram marcadores `data-sc-*`, que o passo seguinte transforma em
// <strong> e <em>.
const ATRIBUTO_STYLE = /([\s/])style\s*=\s*(?:"([^"]*)"|'([^']*)'|([^\s>]+))/gi;

function tirarEstilo(html) {
  return html.replace(ATRIBUTO_STYLE, (_, separador, aspas, apostrofos, sem) => {
    const estilo = (aspas ?? apostrofos ?? sem ?? '').toLowerCase();
    const negrito = /font-weight:\s*(bold|[6-9]00)/.test(estilo) ? ' data-sc-negrito' : '';
    const italico = /font-style:\s*italic/.test(estilo) ? ' data-sc-italico' : '';

    return separador + (negrito + italico).trim();
  });
}

function estiloEmMarcas(doc) {
  doc.body.querySelectorAll('[data-sc-negrito], [data-sc-italico]').forEach((no) => {
    let marcado = doc.createElement('span');
    marcado.append(...no.childNodes);
    if (no.hasAttribute('data-sc-italico')) {
      const em = doc.createElement('em');
      em.appendChild(marcado);
      marcado = em;
    }
    if (no.hasAttribute('data-sc-negrito')) {
      const forte = doc.createElement('strong');
      forte.appendChild(marcado);
      marcado = forte;
    }
    no.appendChild(marcado);
    no.removeAttribute('data-sc-negrito');
    no.removeAttribute('data-sc-italico');
  });
}

function textoDaTabela(tabela) {
  return Array.from(tabela.querySelectorAll('td, th'))
    .map((celula) => celula.textContent.replace(/[\s\xa0]+/g, ' ').trim())
    .filter((texto) => texto)
    .join(' ');
}

// Monta a grade de textos: cada celula ocupa a area do seu colspan/rowspan, o texto na primeira
// posicao e as demais vazias. Devolve `null` se a tabela nao tem celula.
function grade(tabela, aviso) {
  const ocupado = [];
  const celulas = [];
  let largura = 0;

  Array.from(tabela.rows).forEach((linha, l) => {
    ocupado[l] ??= [];
    celulas[l] ??= [];
    let c = 0;

    Array.from(linha.cells).forEach((celula) => {
      while (ocupado[l][c]) {
        c++;
      }

      const colunas = mescla(celula.getAttribute('colspan'));
      const linhas = mescla(celula.getAttribute('rowspan'));

      if (colunas > 1 || linhas > 1) {
        aviso(AVISO_MESCLADA);
      }

      for (let dl = 0; dl < linhas; dl++) {
        ocupado[l + dl] ??= [];
        celulas[l + dl] ??= [];
        for (let dc = 0; dc < colunas; dc++) {
          ocupado[l + dl][c + dc] = true;
          celulas[l + dl][c + dc] = dl === 0 && dc === 0 ? celula : null;
        }
      }

      c += colunas;
      largura = Math.max(largura, c);
    });
  });

  // Linhas que so existem por causa de um rowspan que passou da ultima linha ficam de fora.
  const total = tabela.rows.length;

  return largura && total ? { celulas: celulas.slice(0, total), largura } : null;
}

function reconstruirTabela(tabela, doc, aviso) {
  const montada = grade(tabela, aviso);

  if (!montada) {
    tabela.remove();

    return;
  }

  const nova = doc.createElement('table');
  const corpo = doc.createElement('tbody');

  montada.celulas.forEach((linha, l) => {
    const tr = doc.createElement('tr');

    for (let c = 0; c < montada.largura; c++) {
      const celulaNova = doc.createElement(l === 0 ? 'th' : 'td');
      const p = doc.createElement('p');

      if (linha[c]) {
        emLinha(linha[c], p, doc);
      }

      celulaNova.appendChild(p);
      tr.appendChild(celulaNova);
    }

    corpo.appendChild(tr);
  });

  nova.appendChild(corpo);
  tabela.replaceWith(nova);
}

/**
 * Limpa o HTML colado.
 * @param {string} html HTML da area de transferencia.
 * @returns {{ html: string, avisos: string[] }} HTML limpo e os avisos (sem repeticao) do que nao
 *   e suportado.
 */
export function limparColagem(html) {
  const avisos = [];
  const aviso = (texto) => {
    if (!avisos.includes(texto)) {
      avisos.push(texto);
    }
  };

  const doc = new DOMParser().parseFromString(tirarEstilo(String(html ?? '')), 'text/html');
  const corpo = doc.body;

  const comentarios = [];
  const percorredor = doc.createTreeWalker(corpo, NodeFilter.SHOW_COMMENT);
  while (percorredor.nextNode()) {
    comentarios.push(percorredor.currentNode);
  }
  comentarios.forEach((c) => c.remove());

  corpo.querySelectorAll(REMOVIDOS).forEach((no) => no.remove());

  // Elementos de outros namespaces (<o:p>, <v:shape>) chegam como elementos comuns com ':' no nome.
  Array.from(corpo.querySelectorAll('*'))
    .filter((no) => no.tagName.includes(':'))
    .forEach(desembrulhar);

  if (corpo.querySelector('img')) {
    corpo.querySelectorAll('img').forEach((no) => no.remove());
    aviso(AVISO_IMAGEM);
  }

  estiloEmMarcas(doc);
  corpo.querySelectorAll('[style], [class]').forEach((no) => {
    no.removeAttribute('style');
    no.removeAttribute('class');
  });

  // Tabela dentro de tabela: de dentro para fora, vira o texto dela na celula da externa.
  Array.from(corpo.querySelectorAll('table')).reverse().forEach((tabela) => {
    if (tabela.parentElement?.closest('table')) {
      tabela.replaceWith(doc.createTextNode(` ${textoDaTabela(tabela)} `));
      aviso(AVISO_ANINHADA);
    }
  });

  corpo.querySelectorAll('p').forEach((p) => {
    if (vazio(p.textContent)) {
      p.remove();
    }
  });

  Array.from(corpo.querySelectorAll('table')).forEach((tabela) => reconstruirTabela(tabela, doc, aviso));

  return { html: corpo.innerHTML, avisos };
}
