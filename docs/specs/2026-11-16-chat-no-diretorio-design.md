# Link de chat no Diretório — design

**Estado:** aprovada em 2026-11-16; implementação pendente.

## Por quê

Quem acha uma pessoa no Organograma ou no Diretório quase sempre quer falar com ela. Com a instalação configurada para o Teams, cada pessoa ganha um botão que abre a conversa.

## O que faz

- Botão de chat ao lado de cada pessoa em três lugares: cartão do Organograma, página da pessoa e lista do Diretório.
- Clicar abre o Teams em outra aba, já na conversa com a pessoa.
- Sem configuração, nenhum botão aparece. É o padrão.

## Fora do escopo

- Slack. O desenho fica pronto para ele, mas não é feito agora: o Slack não abre conversa pelo e-mail, precisa do identificador interno da pessoa, o que exige consultar o Slack com credencial.
- Mostrar presença (online, ocupado) ou enviar mensagem pela intranet.
- Campo de "usuário de chat" no perfil.

## Regras

| Assunto | Regra |
|---|---|
| Configuração | `Intranet:Chat:Provedor`: vazio (padrão, sem botão) ou `Teams`. Valor desconhecido derruba a subida, como o modo da CSP |
| Organização | `Intranet:Chat:TenantId`, opcional: o identificador (GUID) da organização no Microsoft, acrescentado ao link. Preenchido com algo que não é GUID, derruba a subida |
| Endereço | `https://teams.microsoft.com/l/chat/0/0?users=<e-mail>`, com o e-mail escapado, mais `&tenantId=<guid>` quando configurado |
| Quem não ganha botão | A própria pessoa logada; pessoa sem e-mail |
| Clique | Outra aba, com `rel="noopener noreferrer"` |
| Texto | "Conversar com {nome} no Teams" (rótulo acessível e `title`) |

**Premissa confirmada pelo Rafael:** o e-mail do Diretório é o mesmo com que a pessoa entra no Teams.

A intranet não chama o Microsoft: só monta o endereço. O formato do link foi conferido na documentação da Microsoft em 2026-11-16, que aceita em `users` o nome principal do usuário, "como um endereço de e-mail".

## Abordagens

1. **Montar o link a partir do e-mail (escolhida).** Sem credencial, sem cadastro por pessoa, sem chamada externa.
2. **Campo de link de chat digitado em cada perfil.** Dá trabalho de cadastro e fica desatualizado.
3. **Consultar o Microsoft para validar cada pessoa.** Exige credencial e cadastro de aplicativo, e não melhora o resultado.

## Como é construído

- Uma peça única no Web, `ILinkDeChat`, recebe a pessoa e quem está vendo e devolve o endereço ou nada. As telas só perguntam a ela. O Slack entra depois como outra implementação da mesma peça.
- O cartão do Organograma é do tema: `NoOrganogramaModel` ganha `ChatUrl`, opcional, e os dois temas desenham o botão. A página da pessoa e a lista do Diretório são views do core.
- A CSP não muda: é navegação para outro site.

## Quebras a registrar no CHANGELOG

- Tema próprio (fork): `NoOrganogramaModel` ganha `ChatUrl`, e o cartão do Organograma ganha a classe `sc-org__chat`. Tema que não desenhe o botão continua funcionando, só não o mostra no Organograma.

## Testes

- Unidade: montagem do endereço (e-mail com `+` e acento, sem e-mail, a própria pessoa, provedor desligado, com e sem `TenantId`); configuração inválida derruba a subida.
- Integração: as três telas com e sem configuração; a própria pessoa sem botão; `target` e `rel`.
- Tela (opt-in): Organograma nos dois temas, com o botão dentro do cartão sem recolher a equipe ao clicar; CSP sem violação.
