// Editor do corpo do aviso: melhoria progressiva sobre o textarea que o core renderiza. Sem este
// script, ou se o editor nao subir, o formulario segue valendo — o campo aceita markdown cru.
// O textarea continua sendo o campo enviado; o editor so o mantem em dia.
(function () {
  'use strict';

  var area = document.querySelector('[data-sc-editor-markdown]');

  if (!area || typeof seccoEditor === 'undefined') {
    return;
  }

  function el(tag, classe, atributos) {
    var no = document.createElement(tag);

    if (classe) {
      no.className = classe;
    }

    Object.keys(atributos || {}).forEach(function (nome) {
      no.setAttribute(nome, atributos[nome]);
    });

    return no;
  }

  function botao(rotulo, icone) {
    var b = el('button', 'btn btn-sm btn-outline-secondary', { type: 'button', 'aria-label': rotulo, title: rotulo });
    b.appendChild(el('i', 'bi ' + icone, { 'aria-hidden': 'true' }));

    return b;
  }

  var conteiner = el('div', 'sc-editor');
  var barra = el('div', 'sc-editor__barra', { role: 'toolbar', 'aria-label': 'Formatação do texto' });
  var avisos = el('div', 'sc-editor__avisos', { 'aria-live': 'polite' });
  var texto = el('div', 'sc-editor__texto');

  conteiner.appendChild(barra);
  conteiner.appendChild(texto);
  conteiner.appendChild(avisos);
  area.parentNode.insertBefore(conteiner, area);

  var editor;

  try {
    editor = seccoEditor.criarEditor({
      elemento: texto,
      markdown: area.value,
      aoMudar: function (markdown) {
        area.value = markdown;
      }
    });
  } catch (erro) {
    // Desiste sem deixar rastro. Esconder o textarea ANTES de saber que o editor subiu
    // trocaria "sem editor" por "formulario inutilizavel", o oposto de melhoria progressiva.
    conteiner.remove();
    console.error('Editor do mural indisponivel; o campo de texto simples segue valendo.', erro);

    return;
  }

  area.classList.add('d-none');

  // O botao nao rouba o foco do texto: a selecao segue valendo e quem digita logo depois do
  // clique nao perde as primeiras letras.
  barra.addEventListener('mousedown', function (evento) {
    if (evento.target.closest('button')) {
      evento.preventDefault();
    }
  });

  var atualizadores = [];

  function comando(rotulo, icone, executar, ativo) {
    var b = botao(rotulo, icone);

    b.addEventListener('click', function () {
      executar(editor.chain().focus()).run();
    });

    if (ativo) {
      atualizadores.push(function () {
        var ligado = ativo();
        b.classList.toggle('active', ligado);
        b.setAttribute('aria-pressed', ligado ? 'true' : 'false');
      });
    }

    return b;
  }

  function grupo(botoes, atributos) {
    var g = el('div', 'sc-editor__grupo', atributos);
    botoes.forEach(function (b) {
      g.appendChild(b);
    });
    barra.appendChild(g);

    return g;
  }

  grupo([
    comando('Título', 'bi-type-h2', function (c) { return c.toggleHeading({ level: 2 }); },
      function () { return editor.isActive('heading', { level: 2 }); }),
    comando('Subtítulo', 'bi-type-h3', function (c) { return c.toggleHeading({ level: 3 }); },
      function () { return editor.isActive('heading', { level: 3 }); }),
    comando('Negrito', 'bi-type-bold', function (c) { return c.toggleBold(); },
      function () { return editor.isActive('bold'); }),
    comando('Itálico', 'bi-type-italic', function (c) { return c.toggleItalic(); },
      function () { return editor.isActive('italic'); })
  ]);

  grupo([
    comando('Lista', 'bi-list-ul', function (c) { return c.toggleBulletList(); },
      function () { return editor.isActive('bulletList'); }),
    comando('Lista numerada', 'bi-list-ol', function (c) { return c.toggleOrderedList(); },
      function () { return editor.isActive('orderedList'); }),
    comando('Citação', 'bi-quote', function (c) { return c.toggleBlockquote(); },
      function () { return editor.isActive('blockquote'); }),
    comando('Separador', 'bi-dash-lg', function (c) { return c.setHorizontalRule(); })
  ]);

  grupo([
    comando('Código', 'bi-code', function (c) { return c.toggleCode(); },
      function () { return editor.isActive('code'); }),
    comando('Bloco de código', 'bi-code-square', function (c) { return c.toggleCodeBlock(); },
      function () { return editor.isActive('codeBlock'); })
  ]);

  // Link: o endereco vem de um campo na propria barra. prompt() fica fora por ser bloqueante e
  // impossivel de estilizar ou testar.
  var botaoLink = botao('Link', 'bi-link-45deg');
  var campoLink = el('div', 'sc-editor__link input-group input-group-sm', { hidden: '' });
  var entradaLink = el('input', 'form-control', {
    type: 'text', 'aria-label': 'Endereço do link', placeholder: 'https://', autocomplete: 'off'
  });
  var aplicar = el('button', 'btn btn-outline-secondary', { type: 'button' });
  var remover = el('button', 'btn btn-outline-secondary', { type: 'button' });
  aplicar.textContent = 'Aplicar';
  remover.textContent = 'Remover';
  campoLink.appendChild(entradaLink);
  campoLink.appendChild(aplicar);
  campoLink.appendChild(remover);

  function aplicarLink() {
    var destino = entradaLink.value.trim();
    var corrente = editor.chain().focus().extendMarkRange('link');
    var aceito = destino ? corrente.setLink({ href: destino }).run() : corrente.unsetLink().run();

    entradaLink.classList.toggle('is-invalid', !aceito);

    if (aceito) {
      campoLink.hidden = true;
    }
  }

  botaoLink.addEventListener('click', function () {
    campoLink.hidden = !campoLink.hidden;

    if (!campoLink.hidden) {
      entradaLink.value = editor.getAttributes('link').href || '';
      entradaLink.classList.remove('is-invalid');
      entradaLink.focus();
    }
  });
  aplicar.addEventListener('click', aplicarLink);
  remover.addEventListener('click', function () {
    entradaLink.value = '';
    aplicarLink();
  });
  entradaLink.addEventListener('keydown', function (evento) {
    if (evento.key === 'Enter') {
      evento.preventDefault();
      aplicarLink();
    }
  });
  atualizadores.push(function () {
    var ligado = editor.isActive('link');
    botaoLink.classList.toggle('active', ligado);
    botaoLink.setAttribute('aria-pressed', ligado ? 'true' : 'false');
  });

  grupo([
    botaoLink,
    comando('Inserir tabela', 'bi-table', function (c) {
      return c.insertTable({ rows: 3, cols: 2, withHeaderRow: true });
    })
  ]);

  var tabela = grupo([
    comando('Adicionar linha', 'bi-plus-square', function (c) { return c.addRowAfter(); }),
    comando('Remover linha', 'bi-dash-square', function (c) { return c.deleteRow(); }),
    comando('Adicionar coluna', 'bi-layout-sidebar-inset', function (c) { return c.addColumnAfter(); }),
    comando('Remover coluna', 'bi-layout-sidebar', function (c) { return c.deleteColumn(); }),
    comando('Remover tabela', 'bi-trash', function (c) { return c.deleteTable(); })
  ], { hidden: '' });
  atualizadores.push(function () {
    tabela.hidden = !editor.isActive('table');
  });

  grupo([
    comando('Desfazer', 'bi-arrow-counterclockwise', function (c) { return c.undo(); }),
    comando('Refazer', 'bi-arrow-clockwise', function (c) { return c.redo(); })
  ]);

  barra.appendChild(campoLink);

  function atualizar() {
    atualizadores.forEach(function (f) {
      f();
    });
  }

  editor.on('transaction', atualizar);
  atualizar();

  var formulario = area.closest('form');
  if (formulario) {
    formulario.addEventListener('submit', function () {
      area.value = editor.getMarkdown();
    });
  }
})();
