// Preferencias de exibicao do tema Vertical: modo claro/escuro e barra lateral recolhida.
// Ambas sao conveniencia por navegador — nada aqui e autenticacao nem estado de negocio.
(function () {
  'use strict';

  var root = document.documentElement;

  function guardar(chave, valor) {
    try {
      localStorage.setItem(chave, valor);
    } catch (erro) {
      // Armazenamento bloqueado (janela anonima, site data desabilitado): a preferencia
      // vale so para esta pagina. Nao ha o que reportar ao usuario.
    }
  }

  var alternarTema = document.querySelector('[data-sc-theme-toggle]');
  if (alternarTema) {
    alternarTema.addEventListener('click', function () {
      var atual = root.getAttribute('data-bs-theme');
      if (!atual) {
        atual = window.matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
      }

      var proximo = atual === 'dark' ? 'light' : 'dark';
      root.setAttribute('data-bs-theme', proximo);
      alternarTema.setAttribute('aria-pressed', proximo === 'dark' ? 'true' : 'false');
      guardar('sc-theme', proximo);
    });
  }

  var recolher = document.querySelector('[data-sc-sidebar-toggle]');
  if (recolher) {
    recolher.addEventListener('click', function () {
      var recolhida = root.getAttribute('data-sc-sidebar') === 'collapsed';

      if (recolhida) {
        root.removeAttribute('data-sc-sidebar');
        guardar('sc-sidebar', 'expanded');
      } else {
        root.setAttribute('data-sc-sidebar', 'collapsed');
        guardar('sc-sidebar', 'collapsed');
      }

      recolher.setAttribute('aria-expanded', recolhida ? 'true' : 'false');
    });
  }

  // Em telas estreitas a barra lateral vira gaveta sobre o conteudo.
  var barra = document.getElementById('sc-sidebar');
  var abrir = document.querySelector('[data-sc-sidebar-open]');
  var fundo = null;

  function fechar() {
    if (!barra) {
      return;
    }

    barra.classList.remove('is-open');

    if (abrir) {
      abrir.setAttribute('aria-expanded', 'false');
      abrir.focus();
    }

    if (fundo) {
      fundo.remove();
      fundo = null;
    }
  }

  if (abrir && barra) {
    abrir.addEventListener('click', function () {
      barra.classList.add('is-open');
      abrir.setAttribute('aria-expanded', 'true');

      fundo = document.createElement('div');
      fundo.className = 'sc-backdrop';
      fundo.addEventListener('click', fechar);
      document.body.appendChild(fundo);

      var primeiro = barra.querySelector('a, button');
      if (primeiro) {
        primeiro.focus();
      }
    });

    document.addEventListener('keydown', function (evento) {
      if (evento.key === 'Escape' && barra.classList.contains('is-open')) {
        fechar();
      }
    });
  }

  // O aviso de feedback ja chega visivel do servidor; o JS so agenda o fechamento. Se o
  // bundle do Bootstrap nao carregar, o aviso fica na tela ate a proxima navegacao —
  // preferivel a sumir sem ninguem ler.
  var avisos = document.querySelectorAll('[data-sc-toast]');
  if (window.bootstrap && avisos.length) {
    var semAnimacao = window.matchMedia('(prefers-reduced-motion: reduce)').matches;

    Array.prototype.forEach.call(avisos, function (aviso) {
      window.bootstrap.Toast.getOrCreateInstance(aviso, {
        animation: !semAnimacao,
        autohide: true,
        delay: 6000
      }).show();
    });
  }
})();
