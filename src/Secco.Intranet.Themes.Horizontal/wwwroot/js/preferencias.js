// Aplica as preferencias antes da primeira pintura: sem isso a pagina pisca no modo errado a cada
// carregamento. Carregado sincrono no <head>, antes do CSS. Arquivo, e nao script inline, porque a
// CSP da Intranet nao libera script inline (ADR-0057 da plataforma).
(function () {
  // Com JS os submenus viram painéis; sem ele ficam abertos e recuados (ver menu-arvore.js).
  document.documentElement.classList.add('sc-js');
  try {
    var tema = localStorage.getItem('sc-theme');
    if (tema === 'dark' || tema === 'light') {
      document.documentElement.setAttribute('data-bs-theme', tema);
    }
  } catch (erro) {
    // Sem armazenamento: segue a preferencia do sistema operacional.
  }
})();
