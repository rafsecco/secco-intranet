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

  // Um alerta dispensavel por texto, sem repetir o que ja esta na tela. textContent, nunca HTML:
  // o texto e fixo, mas o contrato vale para qualquer aviso que entre aqui.
  function mostrarAvisos(textos) {
    textos.forEach(function (aviso) {
      var repetido = Array.prototype.some.call(avisos.children, function (alerta) {
        return alerta.firstChild.textContent === aviso;
      });

      if (repetido) {
        return;
      }

      var alerta = el('div', 'alert alert-warning alert-dismissible fade show mb-0 mt-2');
      var mensagem = el('span');
      var fechar = el('button', 'btn-close', { type: 'button', 'data-bs-dismiss': 'alert', 'aria-label': 'Fechar' });

      mensagem.textContent = aviso;
      alerta.appendChild(mensagem);
      alerta.appendChild(fechar);
      avisos.appendChild(alerta);
    });
  }

  var AVISO_LINK = 'Este endereço não pode virar link. Use um endereço http, https, mailto ou tel.';
  var AVISO_PENDENTES ='Envie ou remova as imagens pendentes antes de publicar.';
  var editor;
  var posicaoPendente;

  // Seletor de arquivo unico, compartilhado pelo botao Imagem e pelo botao de imagem pendente.
  var seletor = el('input', '', {
    type: 'file', accept: 'image/png,image/jpeg,image/webp', hidden: '', tabindex: '-1', 'aria-hidden': 'true'
  });
  seletor.multiple = true;
  conteiner.appendChild(seletor);

  function escolherArquivos(pendente) {
    posicaoPendente = pendente;
    seletor.multiple = pendente === undefined;
    seletor.click();
  }

  seletor.addEventListener('change', function () {
    var opcoes = posicaoPendente === undefined ? {} : { substituir: posicaoPendente };

    if (seletor.files.length > 0) {
      mostrarAvisos(seccoEditor.inserirArquivos(editor, seletor.files, opcoes));
    }

    posicaoPendente = undefined;
    seletor.value = '';
  });

  try {
    editor = seccoEditor.criarEditor({
      elemento: texto,
      markdown: area.value,
      aoMudar: function (markdown) {
        area.value = markdown;
      },
      aoColar: mostrarAvisos,
      publicacaoId: area.getAttribute('data-sc-publicacao-id') || '',
      aoClicarPendente: escolherArquivos
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
      function () { return editor.isActive('italic'); }),
    comando('Sublinhado', 'bi-type-underline', function (c) { return c.toggleUnderline(); },
      function () { return editor.isActive('underline'); })
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
    } else {
      mostrarAvisos([AVISO_LINK]);
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

  // Imagem: o arquivo entra no texto como marcador e so sobe com o formulario. A descricao (o alt)
  // da imagem selecionada tem um campo proprio na barra, no molde do link.
  var botaoImagem = botao('Imagem', 'bi-image');
  var campoDescricao = el('div', 'sc-editor__descricao input-group input-group-sm', { hidden: '' });
  var entradaDescricao = el('input', 'form-control', {
    type: 'text', 'aria-label': 'Descrição da imagem', placeholder: 'Descrição da imagem', maxlength: '300', autocomplete: 'off'
  });
  var aplicarDescricao = el('button', 'btn btn-outline-secondary', { type: 'button' });
  aplicarDescricao.textContent = 'Aplicar';
  campoDescricao.appendChild(entradaDescricao);
  campoDescricao.appendChild(aplicarDescricao);

  function aplicarDescricaoDaImagem() {
    editor.chain().updateAttributes('image', { alt: entradaDescricao.value.trim().slice(0, 300) }).run();
  }

  botaoImagem.addEventListener('click', function () {
    escolherArquivos(undefined);
  });
  aplicarDescricao.addEventListener('click', aplicarDescricaoDaImagem);
  entradaDescricao.addEventListener('keydown', function (evento) {
    if (evento.key === 'Enter') {
      evento.preventDefault();
      aplicarDescricaoDaImagem();
    }
  });
  atualizadores.push(function () {
    var selecionada = editor.isActive('image');

    campoDescricao.hidden = !selecionada;

    // Quem digita a descricao nao pode ter o campo reescrito a cada transacao.
    if (selecionada && document.activeElement !== entradaDescricao) {
      entradaDescricao.value = editor.getAttributes('image').alt || '';
    }
  });

  grupo([
    botaoLink,
    comando('Inserir tabela', 'bi-table', function (c) {
      return c.insertTable({ rows: 3, cols: 2, withHeaderRow: true });
    }),
    botaoImagem
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

  // Modo Markdown cru: o textarea (o campo enviado) fica a vista no lugar do editor.
  var botaoMarkdown = botao('Markdown', 'bi-markdown');
  var modoCru = false;

  botaoMarkdown.setAttribute('aria-pressed', 'false');
  grupo([botaoMarkdown]);

  barra.appendChild(campoLink);
  barra.appendChild(campoDescricao);

  function atualizar() {
    atualizadores.forEach(function (f) {
      f();
    });
  }

  function alternarModoCru() {
    modoCru = !modoCru;
    botaoMarkdown.classList.toggle('active', modoCru);
    botaoMarkdown.setAttribute('aria-pressed', modoCru ? 'true' : 'false');

    Array.prototype.forEach.call(barra.querySelectorAll('button'), function (b) {
      if (b !== botaoMarkdown) {
        b.disabled = modoCru;
      }
    });

    if (modoCru) {
      area.value = editor.getMarkdown();
      texto.hidden = true;
      area.classList.remove('d-none');
      campoLink.hidden = true;
      campoDescricao.hidden = true;
      tabela.hidden = true;
      area.focus();

      return;
    }

    editor.commands.setContent(area.value, { contentType: 'markdown' });
    area.value = editor.getMarkdown();
    area.classList.add('d-none');
    texto.hidden = false;
    editor.commands.focus();
    atualizar();
  }

  botaoMarkdown.addEventListener('click', alternarModoCru);

  editor.on('transaction', atualizar);
  atualizar();

  var formulario = area.closest('form');
  if (formulario) {
    // Os arquivos das imagens novas vao neste campo, que so existe com o editor ativo.
    var arquivosDoTexto = el('input', '', { type: 'file', name: 'Form.ImagensDoTexto', hidden: '' });
    arquivosDoTexto.multiple = true;
    formulario.appendChild(arquivosDoTexto);

    formulario.addEventListener('submit', function (evento) {
      // No modo cru o campo enviado e o texto que a pessoa escreveu.
      if (!modoCru) {
        area.value = editor.getMarkdown();
      }

      // Imagem que nao veio nao pode ir: o servidor so a descartaria, e o texto perderia o lugar dela.
      if (area.value.indexOf('](pendente:)') !== -1) {
        evento.preventDefault();
        mostrarAvisos([AVISO_PENDENTES]);
        (texto.querySelector('[data-imagem-pendente]') || area).focus();

        return;
      }

      var envio = seccoEditor.paraEnvio(area.value);
      var transferencia = new DataTransfer();

      area.value = envio.markdown;
      envio.arquivos.forEach(function (arquivo) {
        transferencia.items.add(arquivo);
      });
      arquivosDoTexto.files = transferencia.files;
      // Sem arquivo o navegador mandaria uma parte vazia: desabilitado, o campo nao vai.
      arquivosDoTexto.disabled = envio.arquivos.length === 0;
    });
  }
})();
