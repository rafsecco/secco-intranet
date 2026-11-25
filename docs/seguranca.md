# Segurança dos arquivos

Como a intranet protege os arquivos que as pessoas enviam. É para quem avalia a segurança de uma
instalação, como a TI do cliente, o encarregado de dados ou um questionário de segurança. O
detalhe técnico está na [ADR-0005](adr/secco-intranet-adrs.md), e a operação no
[guia de deploy](guia-de-deploy.md).

## O que é guardado como arquivo

- Documentos dos setores e das bibliotecas.
- Imagem de destaque e imagens no texto das publicações do Mural.
- Fotos das pessoas no Diretório.

Todos passam pelo mesmo armazenamento e recebem a mesma proteção. Nenhum tipo de arquivo fica de
fora.

## Cifrados no servidor

Todo arquivo é gravado cifrado, e a cifragem é feita pela própria aplicação, antes de o arquivo
chegar ao disco. Ela não depende de o disco ou o serviço de armazenamento serem cifrados.

- **Algoritmo:** AES-256-GCM. A cifragem vem do SDK da plataforma (`Secco.SDK.EntityFrameworkCore`,
  ADR-0061 da plataforma), não de código próprio.
- **Uma chave por arquivo:** cada arquivo recebe uma chave própria de 256 bits, sorteada no envio.
- **Integridade:** o conteúdo é cifrado em blocos de 64 KiB, e cada bloco é autenticado. Um arquivo
  alterado, truncado ou com blocos trocados de lugar não é aberto. A aplicação recusa em vez de
  entregar conteúdo adulterado.
- **Chave mestra:** a chave de cada arquivo é cifrada por uma chave mestra, também AES-256-GCM, e
  guardada no banco. O arquivo cifrado fica na pasta do servidor.

Para ler um arquivo é preciso ter as três coisas ao mesmo tempo: o banco, a pasta e a chave mestra.
Quem tem só a pasta, ou um backup dela, não lê nada.

## Quem consegue ler

Nenhum arquivo tem endereço público. Toda leitura passa pela aplicação, que confere a permissão de
quem pede a cada acesso e só então decifra e entrega.

| Arquivo | Quem lê |
|---|---|
| Documento de setor | Quem tem leitura no setor, ou todas as pessoas da instituição quando o documento é marcado para a empresa toda |
| Documento de biblioteca | Quem tem leitura na biblioteca |
| Imagens de uma publicação do Mural | Quem pode ver a publicação |
| Foto de uma pessoa | Quem tem acesso ao Diretório |

- **Não confirma a existência:** um documento que não existe e um documento sem permissão recebem a
  mesma resposta.
- **Sem cache compartilhado:** as imagens e as fotos são entregues com cache privado, que só o
  navegador da própria pessoa guarda.
- **Cada instituição separada:** cada uma tem banco próprio e subpasta própria no armazenamento. Uma
  instituição não alcança os arquivos de outra.

## O que fica com quem instala

A cifragem só protege se a chave mestra for bem guardada, e isso é responsabilidade da instalação.

- **Chave obrigatória:** a chave mestra (256 bits) é configurada por quem instala. Sem ela a
  aplicação só sobe no ambiente de desenvolvimento. A chave de desenvolvimento que vem no código é
  recusada em qualquer outro ambiente.
- **Guardar num cofre:** fora do servidor e separada do backup. Chave guardada junto do backup é o
  mesmo que não cifrar.
- **Perder a chave é perder todos os arquivos.** Não há como recuperar.
- **Troca de chave:** a chave nova passa a ser a ativa e a atual vai para as aposentadas. O
  `migrate` de cada deploy reembrulha, para a chave ativa, as chaves dos arquivos que ainda dependem
  de uma aposentada, em todos os tenants. Ele regrava só as chaves no banco, nenhum arquivo. A
  aposentada sai da configuração quando um `migrate` não reembrulha mais nada. Se alguma chave de
  arquivo não abre com nenhuma chave configurada, o `migrate` falha e aponta no log. O procedimento
  está no guia de deploy, na seção [Troca da chave mestra](guia-de-deploy.md#troca-da-chave-mestra).
- **Pasta fora da área pública:** a pasta de armazenamento fica fora da pasta pública da
  aplicação.
- **HTTPS:** a proteção no caminho entre o navegador e o servidor vem do HTTPS da instalação, que a
  aplicação exige fora do ambiente de desenvolvimento.

Como configurar a chave, fazer backup e restaurar está no guia de deploy, nas seções
[Variáveis de ambiente](guia-de-deploy.md#variáveis-de-ambiente) e
[Backup e restauração](guia-de-deploy.md#backup-e-restauração).

## Limites

- **Servidor em funcionamento:** enquanto a aplicação está rodando, ela tem a chave mestra para
  poder entregar os arquivos. Quem controla o servidor em execução consegue ler os arquivos. A
  cifragem protege o disco, os backups e o armazenamento, não um servidor já comprometido.
- **Depois de baixado:** um arquivo baixado sai da proteção da intranet e passa a depender do
  computador de quem baixou.
