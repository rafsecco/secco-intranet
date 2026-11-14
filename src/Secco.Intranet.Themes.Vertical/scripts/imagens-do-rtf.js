// Imagens de um RTF colado do Word. O HTML da area de transferencia aponta para `file:///...`, que o
// navegador nao le; os bytes ficam no `text/rtf`, em hexadecimal, dentro de `{\pict ...}`. Cada imagem
// vem duas vezes: no `\shppict` (PNG ou JPEG, a que interessa) e no `\nonshppict` (metafile, ignorado).
// So PNG e JPEG entram, na ordem do RTF; o resto (WMF, EMF) nao e uma imagem que o editor aceite.

const LIMITE_RTF = 50 * 1024 * 1024;
const ABERTURA_SHPPICT = /\{\\\*\\shppict(?![a-zA-Z])/g;
const ABERTURA_PICT = /\{\\pict(?![a-zA-Z])/g;
const TIPOS = { pngblip: 'image/png', jpegblip: 'image/jpeg' };
const EXTENSOES = { 'image/png': 'png', 'image/jpeg': 'jpg' };

// Posicao logo depois da chave que fecha o grupo aberto em `inicio`. `\{`, `\}` e `\\` nao contam.
function fimDoGrupo(rtf, inicio) {
  let nivel = 0;

  for (let i = inicio; i < rtf.length; i++) {
    const c = rtf[i];

    if (c === '\\') {
      i++;
    } else if (c === '{') {
      nivel++;
    } else if (c === '}' && --nivel === 0) {
      return i + 1;
    }
  }

  return rtf.length;
}

// O texto de cada grupo que comeca na `abertura` (regex global), sem entrar nos grupos ja achados.
function gruposDe(rtf, abertura) {
  const grupos = [];
  let achado;

  abertura.lastIndex = 0;

  while ((achado = abertura.exec(rtf)) !== null) {
    const fim = fimDoGrupo(rtf, achado.index);
    grupos.push(rtf.slice(achado.index, fim));
    abertura.lastIndex = fim;
  }

  return grupos;
}

// O grupo filho (`{\*\picprop{\sp{\sn ..}}}`, `{\*\blipuid ..}`) vira espaco, de dentro para fora e em
// qualquer profundidade, antes de remover as palavras de controle: senao `\bliptag-123` gruda no
// hexadecimal.
function semGruposFilhos(corpo) {
  let atual = corpo;
  let anterior;

  do {
    anterior = atual;
    atual = atual.replace(/\{[^{}]*\}/g, ' ');
  } while (atual !== anterior);

  return atual;
}

function paraBytes(hex) {
  const par = hex.length - (hex.length % 2);
  const bytes = new Uint8Array(par / 2);

  for (let i = 0; i < bytes.length; i++) {
    bytes[i] = Number.parseInt(hex.slice(i * 2, (i * 2) + 2), 16);
  }

  return bytes;
}

function confere(bytes, assinatura) {
  return bytes.length > assinatura.length && assinatura.every((b, i) => bytes[i] === b);
}

const ASSINATURA = {
  'image/png': [0x89, 0x50, 0x4e, 0x47],
  'image/jpeg': [0xff, 0xd8, 0xff],
};

/**
 * @param {string} rtf Conteudo de `text/rtf`.
 * @returns {File[]} As imagens PNG e JPEG dos `\pict` de `\shppict`, na ordem do RTF; vazio se o RTF
 *   passa de 50 MB.
 */
export function imagensDoRtf(rtf) {
  const texto = String(rtf ?? '');

  if (texto.length === 0 || texto.length > LIMITE_RTF) {
    return [];
  }

  const arquivos = [];

  gruposDe(texto, ABERTURA_SHPPICT).forEach((figura) => {
    gruposDe(figura.slice(1), ABERTURA_PICT).forEach((pict) => {
      // Sem a abertura `{\pict`; a chave que fecha sai junto com o resto do que nao e hexadecimal.
      const corpo = semGruposFilhos(pict.slice('{\\pict'.length));
      const palavra = /\\(pngblip|jpegblip)/.exec(corpo)?.[1];

      if (!palavra) {
        return;
      }

      const tipo = TIPOS[palavra];
      const hex = corpo.replace(/\\[a-zA-Z]+-?\d*/g, ' ').replace(/[^0-9a-fA-F]/g, '');
      const bytes = paraBytes(hex);

      if (confere(bytes, ASSINATURA[tipo])) {
        arquivos.push(new File([bytes], `word-${arquivos.length + 1}.${EXTENSOES[tipo]}`, { type: tipo }));
      }
    });
  });

  return arquivos;
}
