# Changelog

Mudanças que exigem ajuste de quem já roda a Intranet. O formato segue
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [Não lançado]

### Quebras — Links do setor

Detalhes na [spec dos links do setor](docs/specs/2026-11-15-links-do-setor-design.md).

**Banco:**

- A migration `ItemMenuNovaAba` acrescenta a coluna `fl_nova_aba` à tabela `tb_itens_menu`. Rode o
  `migrate` antes de subir a versão nova.

**Tema próprio (fork):**

- `NavigationItemModel` ganhou o parâmetro `NovaAba`. Quando for verdadeiro, o tema precisa escrever
  `target="_blank" rel="noopener noreferrer"` no link do menu; sem isso tudo abre na mesma página.

## [0.1.0] - 2026-11-15

Primeira versão marcada. As seções abaixo listam o que muda para quem já rodava a Intranet a partir
da `main`.

### Quebras — Docker e dados da instalação

Detalhes no [guia de deploy](docs/guia-de-deploy.md), no [README](README.md) e na
[ADR-0014](docs/adr/secco-intranet-adrs.md).

**Desenvolvimento:**

- `docker compose up -d` passa a construir a imagem e subir a aplicação, e exige `NUGET_PAT` no
  `.env` (token classic do GitHub com `read:packages`). Para subir só o SQL, use
  `docker compose up -d sqlserver-init`; os scripts `dev.ps1` e `dev.sh` já fazem isso.
- O banco de DEV sai do volume `mssql-data` e passa para `.dados/sql`: o banco recomeça vazio, e o
  volume antigo fica órfão (`docker volume rm <projeto>_mssql-data` para apagá-lo).
- O banco e o login de DEV passam a ser `intranet_alfa` e `intranet_alfa_app`: troque os dois nomes
  na connection string do `.env`.
- Os documentos de DEV saem de `bin/.../documentos` e passam para `.dados/documentos`. Os antigos
  não são migrados.
- A aplicação em container responde só em `127.0.0.1:8080`, porque em Development sem SecureGate ela
  roda em modo aberto.

**Imagem e HML/PRD:**

- Dentro da imagem, `/var/lib/secco-intranet` passa a ser `/var/lib/intranet`. Mounts e variáveis
  que apontem para o caminho antigo precisam mudar.
- Serviço e imagem `secco-intranet-web` passam a ser `intranet-web`, inclusive no `reverse_proxy` do
  proxy e nos comandos de deploy.
- O compose do guia troca os volumes nomeados por pastas do host em `${INTRANET_DADOS:-/srv/intranet}`.
  Quem seguiu o exemplo anterior copia os documentos do volume para a pasta nova antes de subir:

  ```bash
  sudo install -d -o 1654 -g 1654 -m 0700 /srv/intranet/documentos /srv/intranet/chaves
  docker run --rm -v <projeto>_documentos:/de -v /srv/intranet/documentos:/para busybox \
    sh -c 'cp -a /de/. /para/ && chown -R 1654:1654 /para'
  ```

- O exemplo anterior do guia não configurava o tenant: o Compose não interpola chave de mapa, e
  `Secco__Tenancy__Tenants__${TENANT_ID}__ConnectionString` saía com o nome literal. O compose novo usa
  a sintaxe de lista.
- As chaves do DataProtection passam a ficar em `/var/lib/intranet/chaves`, com nome de aplicação
  fixo. Na primeira subida com a versão nova, todos entram de novo uma vez.
- O log chega ao LogStream com `ServiceName` `intranet`: filtro ou painel que use `secco-intranet`
  precisa mudar.

### Quebras — Editor do Mural

O Toast UI deu lugar ao Tiptap. Detalhes na
[spec do editor](docs/specs/2026-11-06-editor-do-mural-design.md).

**Banco:**

- A migration `AumentarCorpoDaPublicacao` leva `ds_corpo` a `nvarchar(max)` no SQL Server e a
  `character varying(20000)` no PostgreSQL. Rode o `migrate` antes de subir a versão nova.

- A migration `ImagensNoTexto` cria a tabela das imagens do texto dos avisos. Rode o `migrate` antes
  de subir a versão nova.

**Avisos existentes:**

- O corpo passa a aceitar até 20.000 caracteres, e tabela acima de 10.000 células estimadas é recusada
  ao publicar e ao editar.
- Aviso com tachado (`~~...~~`) perde esse elemento quando é reaberto e salvo no editor visual. O
  texto guardado não muda até alguém salvá-lo. Imagem no texto (`![](...)`) com endereço externo
  vira marcador de imagem pendente, que a pessoa troca enviando o arquivo.

**Tema próprio (fork):**

- Saíram `vendor/js/toastui-editor.js`, `vendor/js/i18n/pt-br.js`,
  `vendor/css/toastui-editor.css`, `vendor/css/toastui-editor-dark.css` e a classe `.sc-legenda`.
- O editor passa a ser `vendor/js/editor-tiptap.js`, com o global `seccoEditor`, mais
  `wwwroot/js/editor.js`. A view de avisos continua carregando os dois do tema Vertical.
- O CSS do editor fica em cada tema: copie para o seu tema o bloco `.sc-editor` e as regras de tabela
  de `.sc-card__body` do `_components.scss` do Vertical. Sem esse bloco, o editor sobe sem estilo,
  porque o Tiptap não injeta CSS para não violar a CSP.
- O bloco `.sc-editor` ganhou as regras do marcador de imagem pendente (`sc-editor__pendente`) e da
  imagem selecionada no texto: o tema de fork precisa copiá-las também.

**CSP:**

- `EstilosDeTerceirosPermitidos` fica só com `display:none`. Saíram `display: none`,
  `display: block` e `null`, que existiam pelo Toast UI. Código de fork que escreva esses valores em
  `style` passa a ser bloqueado.
