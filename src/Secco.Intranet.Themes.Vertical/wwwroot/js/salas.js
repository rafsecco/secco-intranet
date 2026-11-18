// Reserva de sala: so melhora o formulario. Sem JavaScript a lista de convidados aparece inteira
// e o formulario funciona do mesmo jeito; aqui entra apenas a busca que filtra essa lista.
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
})();
