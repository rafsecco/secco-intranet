# Changelog

Mudanças que exigem ajuste de quem já roda a Intranet. O formato segue
[Keep a Changelog](https://keepachangelog.com/pt-BR/1.1.0/).

## [Não lançado]

## [0.2.0] - 2026-11-26

Segunda versão marcada. Traz as ferramentas do dia a dia (links do setor, link de chat do Teams no
Diretório, links do menu principal e reserva de sala), os atributos do Entra no Diretório, as datas
dos formulários na cultura configurada, a cifragem dos arquivos pelo SDK da plataforma e a troca da
chave mestra pelo `migrate`. As seções de quebras listam o que muda para quem já roda a 0.1.0.

**Antes de subir:** rodar o `migrate`, que aplica as migrations novas e reembrulha as chaves dos
arquivos.

### Pacotes da plataforma

- `Secco.SharedKernel` 0.7.13 → 0.7.14
- `Secco.SDK.EntityFrameworkCore` 0.6.0 → 0.7.0

### Endereços com caractere de controle

Links do setor e item personalizado do menu passam a recusar endereço com caractere de controle
(tab, quebra de linha) em qualquer posição, como os links do menu principal já faziam. O navegador
descarta esses caracteres, e `/<TAB>/outro.host` passava por caminho local para virar
`//outro.host`, outro domínio. A regra mora num lugar só (`RotaEhValida`). Um link já gravado com
esse caractere segue funcionando até ser editado.

### Cifragem dos arquivos

Detalhes na [spec](docs/specs/2026-11-25-cifragem-de-arquivo-pelo-sdk-design.md) e na ADR-0005.

A cifragem dos arquivos (documentos, imagens do Mural, fotos do Diretório) passa a ser a do
`Secco.SDK.EntityFrameworkCore` (`ISeccoFileCipher`, ADR-0061 da plataforma, secco-platform#54):
formato `SECCOENC` v1, blocos de 64 KiB. O `EnvelopeCipher` próprio saiu. A troca da chave
mestra está na seção "Troca da chave mestra", abaixo.

### Troca da chave mestra

Detalhes na [spec](docs/specs/2026-11-25-troca-da-chave-mestra-design.md) e na ADR-0005.

O `migrate` passa a reembrulhar, em todos os tenants, as chaves dos arquivos (documentos, imagem de
destaque e imagem no texto do Mural, foto do Diretório) embrulhadas por uma chave aposentada para a
chave ativa, logo depois das migrations de cada tenant. Nenhum arquivo é lido nem regravado, só a
coluna da chave, e não há migration de banco. O log traz, por tenant, a tabela, as chaves
reembrulhadas e as "sem chave conhecida", nunca chave nem caminho.

**Falha:** se uma chave de arquivo não abre com nenhuma chave configurada (em geral, uma aposentada
retirada cedo demais), o tenant conta como falho, o `migrate` sai com código 1 e o seed de referência
não roda. Os demais tenants e as outras chaves do tenant são reembrulhados mesmo assim.

**Procedimento:** o guia de deploy ganha a seção "Troca da chave mestra". A aposentada só pode sair da
configuração depois que todos os containers estiverem na configuração nova e um `migrate` terminar
com código 0 sem reembrulhar nenhuma chave em nenhum tenant.

### Quebras — Cifragem dos arquivos

**Arquivos:** os gravados antes, no formato antigo (`SCCOFILE`), não abrem mais. Para limpar, apague
os arquivos e os registros que apontam para eles, ou zere o banco e a pasta de armazenamento de dev.

### Reserva de sala

Detalhes na [spec](docs/specs/2026-11-16-reserva-de-sala-design.md).

O `intranet-admin` cadastra as salas em `/salas/gerenciar`; qualquer pessoa logada vê a ocupação do dia em `/salas`, reserva um horário livre (de 15 em 15 minutos, no mesmo dia) com convidados do Diretório, e acompanha as suas em "Minhas reservas". A Intranet impede duas reservas da mesma sala no mesmo horário, também com gravações simultâneas, e funciona sozinha. O item "Salas" entra no menu principal de toda instalação.

**Espelhamento (opcional):** com `Intranet__Agenda__Provedor=Microsoft`, um processo em segundo plano envia cada reserva à agenda do Microsoft 365 (cria, atualiza e apaga o evento, pelo Microsoft Graph, a partir de uma caixa de serviço). Vazio, que é o padrão, nada sai da Intranet. Reservas anteriores a ligar o espelhamento não são enviadas. Chaves: `Intranet__Agenda__*` e `Intranet__Salas__FusoHorario` (padrão `America/Sao_Paulo`). O passo a passo do administrador do Microsoft 365 está no guia de deploy, seção "Espelhar as reservas no Microsoft 365".

**Banco:** rodar o `migrate` antes de subir a versão (migration `SalasEReservas`, gerada em 2026-11-25, depois da `LinksDoMenuPrincipal`; tabelas `tb_salas`, `tb_reservas`, `tb_convidados_das_reservas` e `tb_exclusoes_de_evento_pendentes`).

### Datas nos formulários

Os campos de data das salas, dos vencimentos e dos avisos do setor deixam de usar o seletor do navegador, que mostra o formato do idioma do navegador (mm/dd/aaaa, AM/PM), e passam a ser texto no padrão da cultura configurada em `Intranet__Formatos__Cultura` (padrão `pt-BR`: dd/mm/aaaa e 24 horas). O formato aaaa-mm-dd continua aceito. Ao digitar, os separadores entram sozinhos (26112030 vira 26/11/2030), por uma máscara gerada da mesma cultura. Data que não pode ser lida volta com o texto digitado e a mensagem do formato esperado. Na reserva de sala, início e fim viram listas de 15 em 15 minutos, a grade mostra o dia por extenso, o formulário mostra a capacidade da sala com aviso quando os participantes passam dela, e o `intranet-admin` ganha o atalho "Gerenciar salas".

**Correção:** a aplicação não subia em Development sem provedor de agenda configurado (o processamento do espelhamento era registrado sem a agenda).

### Quebras — Datas nos formulários

**Integração por formulário:** quem envia datas aos formulários sem passar pela tela precisa usar dd/mm/aaaa (data e hora: dd/mm/aaaa hh:mm) ou o ISO aaaa-mm-dd(Thh:mm). Outros formatos que o leitor padrão aceitava, como mm/dd/aaaa, deixam de valer.

**Tema próprio (fork):** os campos `Form.DataVencimento`, `novaData`, `Form.PublicadoEm` e `Form.ExpiraEm` são texto, sem `type="date"`/`datetime-local`; script de tema que dependa do seletor nativo precisa ser revisto. `_ViewImports` passa a injetar `FormatoDeDatas` como `Formato`.

### Quebras — Reserva de sala

**Setores:** o slug `salas` passa a ser reservado. Um setor com esse slug sai do menu até ser renomeado.

**Menu principal:** a chave `salas` fica reservada para o item fixo; item de extensão (RCL) com essa chave deixa de aparecer, com aviso no log.

**Tema próprio (fork):** a grade usa classes `sc-salas` novas no CSS do tema e um `salas.js`. Tema que não as tenha mostra a grade sem o desenho.

**Código que chama a navegação:** `IntranetNavigation.PrincipalNaOrdemPadrao` e `NavigationRequest` ganharam o parâmetro `MostrarSalas`, antes de `LinksDoMenu` no `NavigationRequest`; chamada posicional precisa ser ajustada.

### Links do menu principal

Detalhes na [spec](docs/specs/2026-11-18-links-do-menu-principal-design.md) e na ADR-0016.

O `intranet-admin` cadastra, edita e exclui links no card **Links** de `/menu`; eles aparecem no
grupo principal do menu para todos, depois dos extras, e entram na ordenação.

**Banco:** rodar o `migrate` antes de subir a versão (migration `LinksDoMenuPrincipal`, tabela
`tb_links_do_menu_principal`).

**Extensões (RCL):** item de menu principal cuja chave comece com `link-` deixa de aparecer, com
aviso no log; troque a chave.

### Atributos do Entra no Diretório

Detalhes na [spec](docs/specs/2026-11-17-atributos-do-entra-no-diretorio-design.md) e na ADR-0015.

**Dependências:** `Secco.SecureGate.Client` 0.26.0, `Secco.SharedKernel` 0.7.13,
`Secco.SDK.AspNetCore` 0.10.3 e `Secco.SDK.ClientCredentials` 0.1.23.

**SecureGate:** o Diretório passa a ler as pessoas com o escopo `directory:read` do client de gestão
(`Secco:SecureGate:TenantManagement`), que o guia de deploy já mandava conceder. Sem ele, nada
quebra: o Diretório volta à leitura de antes, sem os atributos do Entra, e avisa no log. Para ter os
atributos, o SecureGate precisa da versão com a ADR-0059 e do `User.Read.All` concedido no Entra
(ver o README do SecureGate).

**Comportamento:** cargo, setor e gestor que vêm do Entra ficam só leitura na edição da pessoa e são
ignorados (com aviso na linha) na importação por CSV. Os valores manuais guardados não são apagados.

**Tema próprio (fork):** `PessoaDto` ganhou `Telefone`, `CargoDoEntra`, `SetorDoEntra` e
`GestorDoEntra` (opcionais, no fim); `LinhaDoRelatorio` ganhou `Aviso`.

### Quebras — Link de chat no Diretório

Detalhes na [spec do link de chat](docs/specs/2026-11-16-chat-no-diretorio-design.md).

**Tema próprio (fork):**

- `NoOrganogramaModel` ganhou o parâmetro opcional `ChatUrl`, e o cartão do Organograma ganhou a
  classe `sc-org__chat`. Tema que não desenha o botão continua funcionando, só não o mostra no
  Organograma.

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
