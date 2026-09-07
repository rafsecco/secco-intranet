// Editor de markdown do mural: melhoria progressiva sobre o textarea que o core renderiza.
// Sem este script, o formulario continua funcionando — o campo apenas aceita markdown cru.
(function () {
  'use strict';

  var area = document.querySelector('[data-sc-editor-markdown]');

  if (!area || typeof toastui === 'undefined') {
    return;
  }

  var hospedeiro = document.createElement('div');
  area.parentNode.insertBefore(hospedeiro, area);

  var editor;

  try {
    editor = new toastui.Editor({
      el: hospedeiro,
      height: '320px',
      // Abre em WYSIWYG: quem publica um comunicado nao precisa conhecer markdown.
      initialEditType: 'wysiwyg',
      previewStyle: 'vertical',
      initialValue: area.value,
      usageStatistics: false,
      language: 'pt-BR'
    });
  } catch (erro) {
    // Desiste sem deixar rastro. Esconder o textarea ANTES de saber que o editor subiu
    // trocaria "sem editor" por "formulario inutilizavel", que e o oposto de melhoria
    // progressiva — e foi exatamente o que aconteceu quando o bundle vinha incompleto.
    hospedeiro.remove();
    console.error('Editor de markdown indisponivel; o campo de texto simples segue valendo.', erro);

    return;
  }

  area.style.display = 'none';

  // O editor traz folha propria e nao enxerga os tokens do tema: sem isto ele fica branco
  // dentro do shell escuro. Acompanha o data-bs-theme, inclusive quando o usuario alterna
  // com a pagina aberta.
  var raiz = document.documentElement;

  function aplicarModo() {
    var modo = raiz.getAttribute('data-bs-theme');

    if (!modo) {
      modo = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
    }

    hospedeiro.classList.toggle('toastui-editor-dark', modo === 'dark');
  }

  aplicarModo();
  new MutationObserver(aplicarModo).observe(raiz, { attributes: true, attributeFilter: ['data-bs-theme'] });
  window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', aplicarModo);

  // O textarea continua sendo o campo enviado: o editor so o mantem em dia.
  editor.on('change', function () {
    area.value = editor.getMarkdown();
  });

  var formulario = area.closest('form');
  if (formulario) {
    formulario.addEventListener('submit', function () {
      area.value = editor.getMarkdown();
    });
  }

  // Legenda da sintaxe. Abre por clique — so hover excluiria teclado e toque — e tambem
  // responde ao passar do mouse, para quem descobre a funcao explorando.
  var legenda = document.createElement('div');
  legenda.className = 'sc-legenda';
  legenda.innerHTML =
    '<button class="sc-legenda__botao" type="button" aria-expanded="false">' +
    '<i class="bi bi-question-circle" aria-hidden="true"></i> Sintaxe do Markdown</button>' +
    '<div class="sc-legenda__conteudo" hidden>' +
    '<p><code>**negrito**</code> deixa o texto <strong>em negrito</strong></p>' +
    '<p><code>*italico*</code> deixa o texto <em>em italico</em></p>' +
    '<p><code># Titulo</code> cria um titulo</p>' +
    '<p><code>- item</code> cria um item de lista</p>' +
    '<p><code>[texto](endereco)</code> cria um link</p>' +
    '</div>';

  hospedeiro.parentNode.insertBefore(legenda, hospedeiro.nextSibling);

  var botao = legenda.querySelector('.sc-legenda__botao');
  var conteudo = legenda.querySelector('.sc-legenda__conteudo');

  function mostrar(visivel) {
    conteudo.hidden = !visivel;
    botao.setAttribute('aria-expanded', visivel ? 'true' : 'false');
  }

  // O clique precisa de estado proprio. Decidir pela visibilidade atual nao funciona no
  // mouse: o mouseenter ja abriu a legenda antes do clique chegar, entao o clique a
  // fecharia e o botao pareceria morto. O hover so mostra de passagem; o clique fixa.
  var fixado = false;

  botao.addEventListener('click', function () {
    fixado = !fixado;
    mostrar(fixado);
  });

  legenda.addEventListener('mouseenter', function () {
    mostrar(true);
  });

  legenda.addEventListener('mouseleave', function () {
    if (!fixado) {
      mostrar(false);
    }
  });
})();
