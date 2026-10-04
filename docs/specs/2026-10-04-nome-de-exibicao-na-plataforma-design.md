# Nome de exibição vindo da plataforma — desenho

**Data:** 2026-10-04
**Estado:** rascunho, aguardando revisão
**Complementa:** [2026-09-24-diretorio-organizacional-design.md](2026-09-24-diretorio-organizacional-design.md) — a linha "Nome de exibição" daquela spec previa exatamente esta troca ("local agora, atrás de uma porta; quando a plataforma entregar o `displayName`, o adaptador troca a origem")

## Problema

O Diretório guarda o nome de exibição numa coluna local (`PerfilColaborador.NomeExibicao`)
porque o SecureGate não tinha esse dado. A plataforma entregou o `displayName` no
`Secco.SecureGate.Client` 0.12.0 ([secco-platform#30](https://github.com/rafsecco/secco-platform/issues/30)):
campo no `UserDto`, edição pelo próprio dono em `/conta/nome` do SecureGate e pelo admin via
`SetUserDisplayName`. Com dois lugares guardando o nome, cada um pode dizer uma coisa.

Este é o último item aberto da Fase 1 (MVP). A foto do colaborador, que a spec do Diretório
punha como última etapa, **sai do MVP**: fica para quando a SDK de imagem
([secco-platform#29](https://github.com/rafsecco/secco-platform/issues/29)) existir.

## Decisões

| Eixo | Decisão |
|---|---|
| Dono do nome | O SecureGate. A Intranet só lê e grava pela API; não guarda cópia |
| Onde o colaborador edita | Continua no "Meu perfil" da Intranet. O formulário não muda; o handler grava o nome no SecureGate. O admin do diretório e a importação CSV usam o mesmo caminho |
| Coluna local | Sai nesta entrega, por migration. Sem cópia dos nomes: não há instalação em uso, e em DEV os nomes vêm da lista fictícia |
| Limite | 160 caracteres, o da plataforma (hoje a Intranet limita a 120) |
| Foto | Fora do MVP. Avatar por iniciais continua. Upload, recorte, redução e o partial `_Avatar` ficam para depois da #29 |

## Leitura

- `Secco.SecureGate.Client` sobe da 0.11.0 para a versão estável mais recente, conferida no
  NuGet na hora (0.14.0 em 2026-10-04).
- `UsuarioDto` da gestão de acesso ganha `Nome` (o `displayName`, pode ser nulo).
- `UsuarioParaDiretorio` ganha `Nome`. O adaptador do SecureGate preenche com o `displayName`;
  o adaptador de DEV, com o `Nome` de `PessoasDeDesenvolvimento`. Nenhuma chamada extra: o
  nome vem na mesma listagem, que já tem cache de 60 s por tenant.
- `MontadorDePessoas` usa `UsuarioParaDiretorio.Nome`; vazio, cai no e-mail (como hoje), e sem
  e-mail, no id.

## Escrita

- `IGestaoDeAcesso` ganha `DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome)` sobre
  `SetUserDisplayName`. `null` ou vazio limpa o nome. `GestaoDeAcessoIndisponivel` devolve a
  mesma falha "indisponível" dos outros métodos.
- `EditarContatoHandler`:
  - valida o nome contra 160 caracteres;
  - compara o nome enviado (aparado) com o atual e **só chama o SecureGate se mudou** —
    editar só ramal ou "sobre" nunca depende da plataforma;
  - quando muda, grava primeiro no SecureGate; se falhar, devolve o erro e **não** grava o
    perfil local;
  - depois de gravar o nome, invalida o cache da listagem de usuários do tenant, para a
    pessoa ver o nome novo na hora. A invalidação fica atrás de uma porta pequena
    (`IUsuariosParaDiretorio.Esquecer()`), implementada pelo adaptador do SecureGate e vazia
    nos outros.
- `ImportarDiretorioHandler`: a coluna nome do CSV segue o mesmo caminho, uma chamada por
  linha **cujo nome muda**. Recusa da plataforma conta a linha como "com erro" e o lote
  continua. O cache é invalidado uma vez, no fim do lote.
- `PerfilColaborador` perde `NomeExibicao`, `NomeMaxLength` e o nome em `EditarContato`;
  migration nos dois engines remove a coluna.

Em DEV aberto (sem SecureGate), mudar o nome devolve a mensagem de gestão de acesso
indisponível, e o resto do formulário não é salvo junto — mesma regra de quando o SecureGate
real está fora.

## Auditoria

Sem verbo novo. `diretorio.perfil-editar` já registra os **nomes** dos campos alterados, e
"nome" entra na lista quando muda. A plataforma também audita a mudança do lado dela.

## Testes

- **Unidade**
  - `MontadorDePessoas`: usa `Nome`; vazio cai no e-mail;
  - `EditarContatoHandler`: não chama a gestão quando o nome não mudou; chama com o nome
    aparado quando mudou; falha da gestão impede gravar o perfil local; 161 caracteres é
    recusado; invalida o cache depois de gravar o nome;
  - `ImportarDiretorioHandler`: só chama a gestão para linhas que mudam o nome; recusa conta
    como erro e não interrompe o lote.
- **Integração**
  - editar pelo formulário "Meu perfil" e pela edição do admin chega ao dublê da gestão com o
    nome certo;
  - a migration remove a coluna (o teste de persistência do perfil continua passando sem ela).
- **Fumaça contra o SecureGate real** (`SecureGateGestaoDeAcessoFumacaTests`, roda pelo
  [roteiro de fumaça](../roteiro-fumaca-securegate.md)): gravar um nome, ler de volta na
  listagem, limpar e ler de novo.

## Documentação

- `docs/roadmap.md`: Diretório organizacional entregue; a foto vira item próprio numa fase
  posterior, dependente da #29.
- `docs/plataforma.md`: a linha da #30 deixa de dizer "trocar de adaptador ainda não feito".
- Spec do Diretório: nota no topo apontando para esta spec, e a etapa 6 (foto) marcada como
  fora do MVP.

## Fora de escopo

- A foto (upload, recorte, redução, `_Avatar`).
- Mostrar o `displayName` nas telas de Acesso, que hoje mostram e-mail.
