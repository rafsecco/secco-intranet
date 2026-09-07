// Entrada do bundle do editor. O `dist/toastui-editor.js` publicado NAO e autocontido:
// ele externaliza oito pacotes do ProseMirror, e a versao 3.2.2 nao traz um build "all".
// Servi-lo direto quebra com "Cannot read properties of undefined (reading 'PluginKey')".
// Por isso empacotamos aqui, gerando um IIFE que expoe o global `toastui.Editor` que os
// arquivos de i18n do proprio pacote esperam encontrar.
export { default as Editor } from '@toast-ui/editor';
