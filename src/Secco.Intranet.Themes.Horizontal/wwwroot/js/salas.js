// Reserva de sala: so melhora o formulario. Sem JavaScript a lista de convidados aparece inteira
// e o formulario funciona do mesmo jeito; aqui entram a busca que filtra essa lista e a capacidade
// da sala escolhida, com o aviso quando os participantes passam dela (o servidor ja desenha o
// estado inicial; o aviso nao impede a reserva).
(function () {
  'use strict';

  // Minusculas e sem acento, para "jose" achar "Jose" e "José".
  function normalizar(texto) {
    return texto.toLowerCase().normalize('NFD').replace(/[̀-ͯ]/g, '');
  }

  function iniciar(lista) {
    var itens = Array.prototype.slice.call(lista.querySelectorAll('.sc-salas__convidado'));
    if (itens.length === 0) { return; }

    var busca = document.createElement('input');
    busca.type = 'search';
    busca.className = 'form-control mb-2';
    busca.placeholder = 'Buscar convidado';
    busca.setAttribute('aria-label', 'Buscar convidado');
    busca.autocomplete = 'off';
    lista.parentNode.insertBefore(busca, lista);

    busca.addEventListener('input', function () {
      var termo = normalizar(busca.value.trim());
      itens.forEach(function (item) {
        var nome = normalizar(item.textContent || '');
        item.hidden = termo !== '' && nome.indexOf(termo) === -1;
      });
    });

    // Enter na busca nao pode enviar o formulario por engano.
    busca.addEventListener('keydown', function (evento) {
      if (evento.key === 'Enter') { evento.preventDefault(); }
    });
  }

  Array.prototype.forEach.call(document.querySelectorAll('[data-salas-convidados]'), iniciar);

  function pessoas(total) {
    return total + (total === 1 ? ' pessoa' : ' pessoas');
  }

  function iniciarCapacidade(formulario) {
    var sala = formulario.querySelector('#reserva-sala');
    var capacidade = formulario.querySelector('[data-salas-capacidade]');
    var lotacao = formulario.querySelector('[data-salas-lotacao]');
    if (!sala || !capacidade || !lotacao) { return; }

    function atualizar() {
      var opcao = sala.options[sala.selectedIndex];
      var lugares = parseInt(opcao ? opcao.getAttribute('data-capacidade') : '', 10);
      // Quem reserva tambem ocupa um lugar.
      var participantes = formulario.querySelectorAll('input[name="Convidados"]:checked').length + 1;

      if (isNaN(lugares)) {
        capacidade.textContent = 'Capacidade não informada.';
        lotacao.hidden = true;
        return;
      }

      capacidade.textContent = 'Capacidade: ' + pessoas(lugares) + '.';
      lotacao.textContent = 'A reserva tem ' + participantes + ' participantes, mais do que a capacidade da sala (' + pessoas(lugares) + ').';
      lotacao.hidden = participantes <= lugares;
    }

    sala.addEventListener('change', atualizar);
    formulario.addEventListener('change', function (evento) {
      if (evento.target && evento.target.name === 'Convidados') { atualizar(); }
    });
    atualizar();
  }

  Array.prototype.forEach.call(document.querySelectorAll('form'), function (formulario) {
    if (formulario.querySelector('[data-salas-capacidade]')) { iniciarCapacidade(formulario); }
  });
})();
