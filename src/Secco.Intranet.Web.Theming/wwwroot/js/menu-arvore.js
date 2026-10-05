// Submenus da arvore do menu (NavigationModel com Filhos), compartilhado pelos temas publicados.
// Contrato de marcacao (ver docs/temas.md): gatilho com data-sc-submenu e aria-controls apontando
// para um <ul class="sc-nav__sub" data-nivel="N">; no com filhos e <li class="sc-nav__node">.
// Escopo proprio de proposito: um nome repetido no theme.js do tema ja quebrou a gaveta do
// celular quando os dois scripts dividiam a mesma funcao.
(function () {
  'use strict';

  // Submenus da arvore dos setores. O servidor entrega a arvore inteira em listas aninhadas;
  // sem este script ela aparece aberta e recuada. Aqui cada lista vira painel flutuante com
  // position: fixed — o menu mora num conteiner com rolagem, e um painel absoluto seria
  // cortado. Em tela estreita (gaveta/painel do celular) o submenu abre recuado no lugar.
  var menu = document.querySelector('.sc-nav');

  if (menu) {
    // O tema diz para onde o nivel 1 abre: data-sc-abre-abaixo no .sc-nav (barra no topo)
    // abre abaixo do item; sem ele (barra lateral), a direita.
    var ABRE_ABAIXO = menu.hasAttribute('data-sc-abre-abaixo');
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
        // O primeiro Esc fecha so o submenu: sem isto, o listener da gaveta/painel no document
        // fecharia tambem o menu inteiro de uma vez.
        evento.stopPropagation();
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

    // Foco saiu de um no aberto (Tab para o proximo item ou para fora do menu): fecha esse no.
    // Sem relatedTarget (clique numa area nao focavel) nao fecha — o clique fora ja cuida disso.
    menu.addEventListener('focusout', function (evento) {
      var destino = evento.relatedTarget;

      if (!destino) {
        return;
      }

      Array.prototype.forEach.call(menu.querySelectorAll('[data-sc-submenu][aria-expanded="true"]'), function (gatilho) {
        if (!gatilho.closest('li').contains(destino)) {
          fecharSubmenu(gatilho);
        }
      });
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
})();
