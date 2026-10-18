// Recorte da foto do colaborador (Cropper.js). Sem JavaScript o formulario continua valido:
// os campos x/y/largura/altura ficam vazios e o servidor usa o quadrado central.
(function () {
  'use strict';

  function iniciar(formulario) {
    var entrada = formulario.querySelector('input[type="file"]');
    var area = formulario.querySelector('[data-recorte-foto-area]');
    var imagem = formulario.querySelector('[data-recorte-foto-imagem]');
    if (!entrada || !area || !imagem || typeof window.Cropper !== 'function') { return; }

    var cropper = null;
    var url = null;

    function campo(nome) { return formulario.querySelector('input[type="hidden"][name="' + nome + '"]'); }

    function limpar() {
      if (cropper) { cropper.destroy(); cropper = null; }
      if (url) { URL.revokeObjectURL(url); url = null; }
      ['x', 'y', 'largura', 'altura'].forEach(function (nome) { campo(nome).value = ''; });
      area.hidden = true;
    }

    entrada.addEventListener('change', function () {
      limpar();
      var arquivo = entrada.files && entrada.files[0];
      if (!arquivo) { return; }

      url = URL.createObjectURL(arquivo);
      imagem.addEventListener('load', function aoCarregar() {
        imagem.removeEventListener('load', aoCarregar);
        area.hidden = false;
        cropper = new window.Cropper(imagem, { aspectRatio: 1, viewMode: 1, checkOrientation: true, autoCropArea: 1 });
      });
      imagem.src = url;
    });

    formulario.addEventListener('submit', function () {
      if (!cropper) { return; }
      // getData(true) devolve inteiros, ja nas coordenadas da imagem orientada.
      var dados = cropper.getData(true);
      campo('x').value = dados.x;
      campo('y').value = dados.y;
      campo('largura').value = dados.width;
      campo('altura').value = dados.height;
    });
  }

  document.querySelectorAll('[data-recorte-foto-form]').forEach(iniciar);
})();
