// Preferencias de exibicao do tema Horizontal: modo claro/escuro e o painel de navegacao
// em tela estreita. Ambas sao conveniencia por navegador — nada aqui e autenticacao nem
// estado de negocio.
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

  // Em tela estreita a navegacao sai da linha do cabecalho e vira um painel suspenso.
  var painel = document.getElementById('sc-topnav');
  var abrir = document.querySelector('[data-sc-nav-open]');
  var fundo = null;

  function fechar() {
    if (!painel) {
      return;
    }

    painel.classList.remove('is-open');

    if (abrir) {
      abrir.setAttribute('aria-expanded', 'false');
    }

    if (fundo) {
      fundo.remove();
      fundo = null;
    }
  }

  if (abrir && painel) {
    abrir.addEventListener('click', function () {
      var estaAberto = painel.classList.contains('is-open');

      if (estaAberto) {
        fechar();
        abrir.focus();

        return;
      }

      painel.classList.add('is-open');
      abrir.setAttribute('aria-expanded', 'true');

      fundo = document.createElement('div');
      fundo.className = 'sc-backdrop';
      fundo.addEventListener('click', function () {
        fechar();
      });
      document.body.appendChild(fundo);

      var primeiro = painel.querySelector('a, button');
      if (primeiro) {
        primeiro.focus();
      }
    });

    document.addEventListener('keydown', function (evento) {
      if (evento.key === 'Escape' && painel.classList.contains('is-open')) {
        fechar();
        abrir.focus();
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
