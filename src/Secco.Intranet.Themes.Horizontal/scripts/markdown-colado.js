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

export function pareceMarkdown(texto) {
  const valor = String(texto ?? '');

  return REGRAS.some((regra) => regra.test(valor));
}
