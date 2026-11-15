// Imagens de um RTF colado do Word. O HTML da area de transferencia aponta para `file:///...`, que o
// navegador nao le; os bytes ficam no `text/rtf`, em hexadecimal, dentro de `{\pict ...}`. A imagem
// alinhada ao texto vem no `\shppict`; a flutuante (com quebra de texto), na propriedade `pib` de um
// `\shp`. As copias que nao interessam ficam no `\nonshppict` e no `\shprslt` (metafile, ou a figura de
// novo para quem nao le formas) e no `\listpicture` (marcador de lista com figura).
// O resultado segue a ordem do RTF, com uma posicao por figura: a que nao e PNG nem JPEG (WMF, EMF)
// vira null, para as seguintes nao trocarem de lugar.

const LIMITE_RTF = 50 * 1024 * 1024;
// Um grupo de copia (pulado inteiro) ou uma figura.
const ABERTURA = /\{\\(?:(nonshppict|shprslt|\*\\listpicture)|pict)(?![a-zA-Z])/g;
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

// O texto de cada `{\pict ...}` fora dos grupos de copia, na ordem do RTF.
function figurasDe(rtf) {
  const figuras = [];
  let achado;

  ABERTURA.lastIndex = 0;

  while ((achado = ABERTURA.exec(rtf)) !== null) {
    const fim = fimDoGrupo(rtf, achado.index);

    if (achado[1] === undefined) {
      figuras.push(rtf.slice(achado.index, fim));
    }

    ABERTURA.lastIndex = fim;
  }

  return figuras;
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
 * @returns {(File | null)[]} Uma posicao por figura do RTF, na ordem: o arquivo PNG ou JPEG, ou null
 *   quando a figura e de outro tipo ou esta corrompida. Vazio se o RTF passa de 50 MB.
 */
export function imagensDoRtf(rtf) {
  const texto = String(rtf ?? '');

  if (texto.length === 0 || texto.length > LIMITE_RTF) {
    return [];
  }

  return figurasDe(texto).map((pict, indice) => {
    // Sem a abertura `{\pict`; a chave que fecha sai junto com o resto do que nao e hexadecimal.
    const corpo = semGruposFilhos(pict.slice('{\\pict'.length));
    const palavra = /\\(pngblip|jpegblip)/.exec(corpo)?.[1];

    if (!palavra) {
      return null;
    }

    const tipo = TIPOS[palavra];
    const hex = corpo.replace(/\\[a-zA-Z]+-?\d*/g, ' ').replace(/[^0-9a-fA-F]/g, '');
    const bytes = paraBytes(hex);

    return confere(bytes, ASSINATURA[tipo])
      ? new File([bytes], `word-${indice + 1}.${EXTENSOES[tipo]}`, { type: tipo })
      : null;
  });
}
