// Imagens do texto que ainda nao foram enviadas. O Markdown guarda um marcador (`nova-imagem:N`) e
// o arquivo fica aqui, no modulo: assim o modo Markdown cru, que desmonta e remonta o editor,
// nao perde a imagem. No envio, `paraEnvio` renumera os marcadores e entrega so os arquivos usados.
export const LIMITE_IMAGENS = 10;

const PREFIXO = 'nova-imagem:';
const MARCADOR = /nova-imagem:(\d+)/g;
const CAMINHO_DA_PUBLICACAO = /^\/publicacoes\/([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\/imagens\/[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

const registro = new Map();
let proximo = 0;
let publicacao = '';

// Quem edita um aviso existente pode manter as imagens dele; as de outra publicacao nao valem.
export function definirPublicacao(id) {
  publicacao = String(id ?? '').trim().toLowerCase();
}

export function registrar(arquivo) {
  const numero = proximo++;

  registro.set(numero, { arquivo, url: URL.createObjectURL(arquivo) });

  return PREFIXO + numero;
}

// A URL que o navegador pode mostrar para um `src` do Markdown, ou null se ele nao e um marcador
// registrado nem uma imagem desta publicacao.
export function urlDe(src) {
  const texto = String(src ?? '').trim();

  if (texto.startsWith(PREFIXO)) {
    const numero = /^\d+$/.test(texto.slice(PREFIXO.length)) ? Number(texto.slice(PREFIXO.length)) : -1;

    return registro.get(numero)?.url ?? null;
  }

  const propria = CAMINHO_DA_PUBLICACAO.exec(texto);

  return propria && publicacao !== '' && propria[1].toLowerCase() === publicacao ? texto : null;
}

// Renumera (0..k-1, na ordem em que aparecem) so os marcadores que o texto ainda usa.
export function paraEnvio(markdown) {
  const novos = new Map();
  const arquivos = [];

  const reescrito = String(markdown ?? '').replace(MARCADOR, (achado, digitos) => {
    const numero = Number(digitos);

    if (!registro.has(numero)) {
      return achado;
    }

    if (!novos.has(numero)) {
      novos.set(numero, arquivos.length);
      arquivos.push(registro.get(numero).arquivo);
    }

    return PREFIXO + novos.get(numero);
  });

  return { markdown: reescrito, arquivos };
}
