// Decide se um texto colado (text/plain) parece Markdown. As regras sao propositalmente
// conservadoras: "Conta: 2 * 3 = 6" segue como texto, "**forte**" e "## Titulo" viram formatacao.
const REGRAS = [
  // titulo, lista, lista numerada, citacao, bloco de codigo
  /^(#{1,6}\s|[-*+]\s|\d+[.)]\s|>\s|```)/m,
  // linha de tabela
  /^\s*\|.*\|\s*$/m,
  // negrito
  /\*\*[^*\n]+\*\*/,
  // link
  /\[[^\]\n]+\]\([^)\s]+\)/,
];

// Tags de abertura que carregam formatacao de verdade. O VS Code manda `text/html` so com div, span,
// br e meta (a cor do tema e o `white-space: pre` vao no style): isso e o texto de sempre, nao
// formatacao. Regex nos nomes, sem DOMParser: ler o `style` do HTML dispararia violacao de CSP.
const TAGS_COM_FORMATACAO = /<(?:p|h[1-6]|ul|ol|li|table|b|strong|i|em|u|a|img|blockquote|pre|code)(?=[\s/>])/i;

/**
 * Diz se o HTML colado nao tem formatacao alguma (so div, span, br, meta e afins).
 * @param {string} html HTML da area de transferencia.
 * @returns {boolean} `true` quando nenhuma tag de formatacao aparece.
 */
export function htmlSemFormatacao(html) {
  return !TAGS_COM_FORMATACAO.test(String(html ?? ''));
}

export function pareceMarkdown(texto) {
  const valor = String(texto ?? '');

  return REGRAS.some((regra) => regra.test(valor));
}
