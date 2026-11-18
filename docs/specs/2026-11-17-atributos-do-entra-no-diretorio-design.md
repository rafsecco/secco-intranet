# Atributos do Entra no Diretório — design

**Estado:** desenho aprovado, a implementar.

## Por quê

O SecureGate passou a entregar, pela API de diretório (`directory:read`), cargo, departamento, telefone comercial e gestor sincronizados do Entra ID (ADR-0059 da plataforma, `Secco.SecureGate.Client` 0.25.0). Na intranet, esses dados vivem no `PerfilColaborador` e são digitados à mão ou importados por CSV (`/diretorio/importar`). Para quem tem o Entra, são dois cadastros do mesmo dado, e o da intranet desatualiza. O objetivo é que o Diretório e o Organograma se montem sozinhos a partir do Entra, sem perder a edição manual de quem não o tem.

## O que faz

- **Leitura:** o Diretório passa a ler as pessoas pela API de diretório do SecureGate. Cada pessoa vem com os atributos do Entra quando eles existem.
- **Precedência por campo:** quando o Entra traz o valor, ele vale. Quando o Entra não traz (vazio, conta local, instalação sem Entra), vale o valor manual ou importado por CSV, como hoje.
- **Organograma:** se monta pelo gestor efetivo de cada pessoa. Com o Entra, isso acontece sem nenhum cadastro manual.
- **Edição da pessoa:** o campo que vem do Entra aparece **só leitura**, com a indicação "vem do Entra". Os demais seguem editáveis.
- **Departamento:** vira setor de lotação pelo nome. O departamento que não casa com nenhum setor aparece numa lista na página de importação.
- **Telefone:** o telefone comercial do Entra entra como campo novo, **ao lado** do Ramal. O Ramal não muda.
- **CSV:** a importação continua igual para quem não tem dado do Entra. Para quem tem, as colunas que o Entra já preenche são ignoradas, com aviso na linha.

## Fora do escopo

- Mapeamento configurável departamento → setor por tenant. Fica para quando aparecer um caso de nome que não dá para alinhar (ver "Abordagens").
- Criar setor automaticamente a partir de departamento. Setor = Role no SecureGate (ADR-0001), e criar papéis a partir de texto vindo do diretório do cliente é decisão de autoridade, não de cadastro.
- Usar o telefone do Entra como Ramal.
- Gravar os atributos do Entra no banco da intranet. Eles são lidos a cada consulta (com o cache curto de hoje) e nunca sobrescrevem o `PerfilColaborador`.
- Nome da pessoa: continua sendo o `displayName` do SecureGate, como hoje.
- Ramal, Sobre e foto: continuam só da intranet.

## Abordagens

**Departamento → setor** (pedido: definir a regra):

1. **Pelo nome. Escolhida.** O departamento casa com o nome de um setor **ativo**, ignorando maiúsculas, acentos e espaços nas pontas. Se o nome não bater, o admin renomeia ou cria o setor, e a próxima leitura já casa. Não precisa de tabela, de tela nem de migration.
2. **Mapeamento configurável por tenant.** Seria uma tabela departamento → setor numa tela de admin: mais robusto quando a TI não padroniza nomes, mas custa entidade, migration nos dois engines, tela e permissão. Adiado.
3. **Pelo nome, com o mapeamento por cima.** Tem o custo da 2. Adiado junto com ela.

**Telefone do Entra e Ramal** (pedido: definir):

1. **Ao lado do Ramal. Escolhida.** O `businessPhones` do Entra costuma trazer o número completo (ex.: +55 11 4000-1234), não o ramal interno, e passa dos 20 caracteres do Ramal. Substituir apagaria o Ramal de quem tem os dois.
2. **Substituir o Ramal.** Só faria sentido se o campo do Entra trouxesse o ramal. Descartada.

O usuário escolheu a 1 nos dois casos.

## Regras

| Assunto | Regra |
|---|---|
| Fonte das pessoas | `GET /api/v1/directory/people` (`directory:read`), paginado até o fim (100 por página), com a credencial de `Secco:SecureGate:TenantManagement`, que o guia de deploy já manda criar com `directory:read`. Entram as pessoas com `isActive = true`, como o filtro de hoje (`Situacao != Desativado`). |
| Sem `directory:read` concedido | Instalação antiga, cujo client não tem o escopo: a emissão do token é recusada. O Diretório **cai para a leitura de hoje** (`securegate:tenant-manage`), sem atributos do Entra, e registra um aviso no log uma vez por processo. Nada quebra. Outras falhas (SecureGate fora) continuam virando "indisponível", como hoje. |
| Origem | O atributo só conta como "do Entra" quando `attributesSource == "directory"` **e** o campo veio preenchido. O SecureGate já manda nulo para membro de outro tenant (ADR-0059). |
| Cargo | Entra preenchido → vale e fica só leitura. Senão → `PerfilColaborador.Cargo`. |
| Gestor | Entra preenchido (`managerId`) → vale e fica só leitura. Senão → `PerfilColaborador.GestorUsuarioId`. Gestor que não está entre as pessoas ativas continua contando como "gestor inativo", como hoje. |
| Setor de lotação | `department` do Entra casado com o nome de um setor ativo, por comparação ordinal depois de `Trim`, minúsculas invariantes e remoção de acentos (decomposição FormD sem as marcas). Casou com **exatamente um** setor → vale e fica só leitura. Casou com nenhum ou com mais de um → vale o `PerfilColaborador.SetorId`, editável. Sem `department` → `PerfilColaborador.SetorId`. |
| Departamento sem setor | Aparece na página de importação do diretório (`/diretorio/importar`), numa seção "Departamentos do Entra sem setor": o texto do departamento e quantas pessoas ativas o têm, com nome ambíguo indicado à parte. Não aparece na página da pessoa nem no organograma. |
| Telefone | Campo novo **Telefone**, só leitura, mostrado quando o Entra trouxe `businessPhone`. Na página da pessoa e na lista do Diretório, ao lado do Ramal. O Ramal segue editável pela pessoa e pelo CSV. |
| Valor manual guardado | O `PerfilColaborador` nunca é sobrescrito pelo Entra. Se o Entra esvaziar um campo, o valor manual que estava guardado volta a valer. |
| Edição (`EditarDadosFuncionais`) | Campo travado pelo Entra não é aplicado mesmo que chegue no POST: o handler mantém o valor guardado para esse campo. A tela mostra o valor do Entra desabilitado, com o selo "vem do Entra". |
| Ciclo de gestor | A validação de ciclo da edição manual (`RegrasDeGestor.CriariaCiclo`) passa a usar o **gestor efetivo** de cada pessoa, com o do Entra quando houver. Senão, um gestor manual poderia fechar um ciclo com arestas do Entra. O organograma mantém a rede de segurança atual contra ciclo. |
| CSV | Para a pessoa com um campo travado pelo Entra, a coluna correspondente (`cargo`, `setor`, `gestor`) é ignorada na prévia e na aplicação, e a linha leva um aviso ("cargo vem do Entra; coluna ignorada"). As outras colunas da linha seguem valendo. Sem dado do Entra, nada muda. |
| Auditoria | Nada novo: os atributos do Entra não são gravados pela intranet. A edição manual segue com a trilha de hoje, e só registra os campos de fato alterados. |
| Desenvolvimento | O provedor de desenvolvimento (`UsuariosParaDiretorioDeDesenvolvimento`) passa a ter algumas pessoas com atributos "do Entra", para a tela poder ser vista sem SecureGate. |

## Modelo (o que muda)

- `UsuarioParaDiretorio` ganha os atributos crus do Entra: cargo, departamento, telefone, gestor (id) e a origem.
- `PessoaDto` ganha:
  - `Telefone`;
  - um marcador por campo travado: `CargoDoEntra`, `SetorDoEntra`, `GestorDoEntra`.

  Cargo, setor e gestor do DTO passam a ser os valores **efetivos**.
- `MontadorDePessoas` aplica a precedência e o casamento departamento → setor. A regra de normalização do nome fica num tipo próprio, testável isolado.
- Departamentos sem setor: consulta nova para a página de importação, calculada das pessoas ativas.

## Segurança

- **Input externo:** os textos vêm do diretório do cliente via SecureGate, que já valida tamanho e caractere de controle (ADR-0059). As views os exibem com o encoding padrão do Razor, nunca como `Html.Raw`. O departamento só é comparado, nunca vira slug, rota ou nome de papel.
- **Isolamento:** a API de diretório usa o tenant do token, e o SecureGate já anula os atributos de membro de outro tenant.
- **Privilégio:** a leitura do diretório pede só `directory:read`, com um token separado do de `securegate:tenant-manage` (store próprio, menor privilégio por chamada).
- **Edição travada no servidor:** campo do Entra não é aplicado pelo handler, mesmo com POST forjado.

## Testes esperados

- **Normalização e casamento de nome:** maiúsculas, acentos, espaços, nenhum setor, dois setores com o mesmo nome normalizado (nenhum casa), setor inativo (não casa).
- **Montador:** precedência por campo, com cada combinação de Entra preenchido ou vazio e manual preenchido ou vazio, e marcadores corretos. Membro de outro tenant (atributos nulos) usa o manual.
- **Organograma:** só com gestores do Entra, montado sem nenhum `PerfilColaborador`.
- **Edição:** campo travado mantém o valor guardado mesmo com POST diferente, e o ciclo é validado contra o gestor efetivo.
- **CSV:** coluna ignorada com aviso para pessoa com campo do Entra; sem Entra, o relatório é idêntico ao de hoje.
- **Fonte:**
  - paginação completa;
  - `isActive = false` fica fora;
  - escopo recusado cai para `tenant-manage` com aviso;
  - SecureGate fora → "indisponível".
- **Departamentos sem setor:** contagem e ambíguos.

## Deixado ao construtor

- Nomes exatos dos tipos e das propriedades novas, e o texto do selo e do aviso do CSV (o pedido usa "vem do Entra").
- Como o adaptador cria o client de diretório: um `HttpClient` nomeado com `SeccoClientCredentialsHandler` e escopo `directory:read`, ou outro caminho equivalente do `Secco.SDK.ClientCredentials`. O token fica em store próprio.
- Se a decisão de casar departamento por nome merece uma ADR na intranet. Proposta: ADR-0015, curta, porque muda a dona dos dados do Diretório quando há Entra.
- Atualizar `Secco.SecureGate.Client` para 0.25.0 em `Directory.Packages.props`, junto com as dependências que ele puxar.
