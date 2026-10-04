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

  // Submenus da arvore dos setores. O servidor entrega a arvore inteira em listas aninhadas;
  // sem este script ela aparece aberta e recuada. Aqui cada lista vira painel flutuante com
  // position: fixed — o menu mora num conteiner com rolagem, e um painel absoluto seria
  // cortado. Em tela estreita (gaveta/painel do celular) o submenu abre recuado no lugar.
  var ABRE_ABAIXO = true; // o nivel 1 sai debaixo da barra
  var menu = document.querySelector('.sc-nav');

  if (menu) {
    var largo = window.matchMedia('(min-width: 768px)');
    var comMouse = window.matchMedia('(hover: hover)');
    var ATRASO_AO_SAIR = 300;

    var painelDe = function (gatilho) {
      return document.getElementById(gatilho.getAttribute('aria-controls'));
    };

    var gatilhoDe = function (no) {
      return no.querySelector(':scope > [data-sc-submenu], :scope > .sc-nav__row > [data-sc-submenu]');
    };

    var posicionar = function (gatilho, painel) {
      painel.style.top = '';
      painel.style.left = '';

      if (!largo.matches) {
        return;
      }

      var base = gatilho.closest('li').getBoundingClientRect();
      var largura = painel.offsetWidth;
      var altura = painel.offsetHeight;
      var abaixo = ABRE_ABAIXO && painel.getAttribute('data-nivel') === '1';
      var topo = abaixo ? base.bottom : base.top;
      var esquerda = abaixo ? base.left : base.right;

      // Sem espaco, abre para o lado oposto.
      if (esquerda + largura > window.innerWidth) {
        esquerda = Math.max(0, (abaixo ? base.right : base.left) - largura);
      }

      if (topo + altura > window.innerHeight) {
        topo = Math.max(0, window.innerHeight - altura);
      }

      painel.style.top = topo + 'px';
      painel.style.left = esquerda + 'px';
    };

    var fecharSubmenu = function (gatilho) {
      var painel = painelDe(gatilho);

      if (!painel) {
        return;
      }

      Array.prototype.forEach.call(painel.querySelectorAll('[data-sc-submenu][aria-expanded="true"]'), fecharSubmenu);
      gatilho.setAttribute('aria-expanded', 'false');
      painel.classList.remove('is-open');
    };

    var fecharIrmaos = function (gatilho) {
      var meu = gatilho.closest('li');

      Array.prototype.forEach.call(meu.parentElement.children, function (irmao) {
        var outro = irmao !== meu ? gatilhoDe(irmao) : null;

        if (outro && outro.getAttribute('aria-expanded') === 'true') {
          fecharSubmenu(outro);
        }
      });
    };

    var fecharTudo = function () {
      Array.prototype.forEach.call(menu.querySelectorAll(':scope > li > [data-sc-submenu], :scope > li > .sc-nav__row > [data-sc-submenu]'), fecharSubmenu);
    };

    var abrirSubmenu = function (gatilho) {
      var painel = painelDe(gatilho);

      if (!painel) {
        return;
      }

      fecharIrmaos(gatilho);
      gatilho.setAttribute('aria-expanded', 'true');
      painel.classList.add('is-open');
      posicionar(gatilho, painel);
    };

    var itensDe = function (painel) {
      return Array.prototype.filter.call(
        painel.querySelectorAll(':scope > li > a, :scope > li > button, :scope > li > .sc-nav__row > a'),
        function (elemento) { return elemento.offsetParent !== null; });
    };

    // Clique, toque e Enter/Espaco (o botao converte em click) abrem e fecham.
    menu.addEventListener('click', function (evento) {
      var gatilho = evento.target.closest('[data-sc-submenu]');

      if (!gatilho || !menu.contains(gatilho)) {
        return;
      }

      if (gatilho.getAttribute('aria-expanded') === 'true') {
        // Com mouse, o hover ja abriu: o clique que vem em seguida e a pessoa "abrindo" o
        // que ja esta aberto, e fecha-lo pareceria defeito. Quem fecha e o mouseleave.
        if (evento.detail > 0 && comMouse.matches && largo.matches) {
          return;
        }

        fecharSubmenu(gatilho);

        return;
      }

      abrirSubmenu(gatilho);

      // detail 0 = veio do teclado: o foco desce para o primeiro item.
      if (evento.detail === 0) {
        var primeiro = itensDe(painelDe(gatilho))[0];

        if (primeiro) {
          primeiro.focus();
        }
      }
    });

    // Mouse: abre ao passar; fecha com atraso, para atravessar na diagonal ate o painel. O
    // painel e descendente do <li> no DOM, entao entrar nele nao conta como sair do <li>.
    Array.prototype.forEach.call(menu.querySelectorAll('.sc-nav__node'), function (no) {
      var gatilho = gatilhoDe(no);
      var espera = null;

      no.addEventListener('mouseenter', function () {
        if (!comMouse.matches || !largo.matches || !gatilho) {
          return;
        }

        window.clearTimeout(espera);
        abrirSubmenu(gatilho);
      });

      no.addEventListener('mouseleave', function () {
        if (!comMouse.matches || !largo.matches || !gatilho) {
          return;
        }

        espera = window.setTimeout(function () { fecharSubmenu(gatilho); }, ATRASO_AO_SAIR);
      });
    });

    // Teclado: setas navegam no painel, seta para a direita abre, Esc/seta para a esquerda
    // fecham e devolvem o foco a quem abriu.
    menu.addEventListener('keydown', function (evento) {
      var alvo = evento.target;
      var painel = alvo.closest('.sc-nav__sub.is-open');

      if (evento.key === 'ArrowRight' && alvo.hasAttribute('data-sc-submenu') && alvo.getAttribute('aria-expanded') !== 'true') {
        evento.preventDefault();
        abrirSubmenu(alvo);
        var primeiro = itensDe(painelDe(alvo))[0];

        if (primeiro) {
          primeiro.focus();
        }

        return;
      }

      if (!painel) {
        return;
      }

      var gatilho = menu.querySelector('[aria-controls="' + painel.id + '"]');

      if (evento.key === 'Escape' || evento.key === 'ArrowLeft') {
        evento.preventDefault();
        fecharSubmenu(gatilho);
        gatilho.focus();

        return;
      }

      if (evento.key === 'ArrowDown' || evento.key === 'ArrowUp') {
        evento.preventDefault();
        var itens = itensDe(painel);
        var posicao = itens.indexOf(alvo.closest('a, button'));
        var proximo = evento.key === 'ArrowDown' ? posicao + 1 : posicao - 1;

        itens[(proximo + itens.length) % itens.length].focus();
      }
    });

    // Fora do menu, ou mudou a geometria: fecha (posicao fixa calculada ficaria errada).
    // Geometria so importa em tela larga: em tela estreita o submenu abre recuado no lugar,
    // e rolar a gaveta (ou o navegador recolher a barra de endereco) nao pode fecha-lo.
    document.addEventListener('click', function (evento) {
      if (!menu.contains(evento.target)) {
        fecharTudo();
      }
    });

    window.addEventListener('resize', function () {
      if (largo.matches) {
        fecharTudo();
      }
    });
    window.addEventListener('scroll', function (evento) {
      // Rolar dentro de um painel aberto nao o fecha.
      if (largo.matches && !(evento.target instanceof Element && evento.target.closest('.sc-nav__sub.is-open'))) {
        fecharTudo();
      }
    }, true);
  }

  // Acao irreversivel: <form data-confirmar="Pergunta?"> pergunta antes de enviar. Sem JS o
  // formulario envia direto — o mesmo de antes deste atributo existir, entao uma falha de
  // script nao trava nada. Delegado no document para valer em formularios de qualquer view.
  document.addEventListener('submit', function (evento) {
    var formulario = evento.target;
    var pergunta = formulario instanceof HTMLFormElement ? formulario.getAttribute('data-confirmar') : null;

    if (pergunta && !window.confirm(pergunta)) {
      evento.preventDefault();
    }
  });
})();
