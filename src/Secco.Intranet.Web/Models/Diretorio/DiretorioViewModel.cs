using Secco.Intranet.Application.Diretorio;

namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Modelo da grade de pessoas.</summary>
/// <param name="Tela">Página de pessoas e setores para o filtro.</param>
/// <param name="Busca">Termo buscado, para repopular o campo.</param>
/// <param name="SetorSlug">Setor filtrado, para repopular o seletor.</param>
/// <param name="PodeImportar">Se quem vê é admin do diretório e portanto vê o botão de importar.</param>
public sealed record DiretorioViewModel(PessoasDaTelaDto Tela, string? Busca, string? SetorSlug, bool PodeImportar);

/// <summary>Modelo do relatório de importação.</summary>
/// <param name="Relatorio">O que acontece (pré-visualização) ou aconteceu (aplicação).</param>
/// <param name="Csv">O texto do arquivo, devolvido no formulário de confirmação para ser revalidado.</param>
public sealed record ImportacaoViewModel(RelatorioDeImportacao Relatorio, string Csv);

/// <summary>Modelo da página de uma pessoa.</summary>
/// <param name="Detalhe">Pessoa, gestor e equipe.</param>
/// <param name="PodeEditar">Se quem vê pode editar (o dono do perfil ou um admin).</param>
/// <param name="EditarUrl">Para onde o botão de editar leva.</param>
public sealed record PessoaViewModel(PessoaDetalheDto Detalhe, bool PodeEditar, string? EditarUrl);

/// <summary>Modelo do organograma.</summary>
/// <param name="Organograma">Árvores e quem ficou fora delas.</param>
public sealed record OrganogramaViewModel(OrganogramaDto Organograma);

/// <summary>Modelo de "Meu perfil".</summary>
/// <param name="Pessoa">A pessoa logada, com os valores atuais.</param>
/// <param name="Form">Formulário de contato.</param>
public sealed record MeuPerfilViewModel(PessoaDto Pessoa, EditarContatoForm Form);

/// <summary>Modelo da edição completa pelo admin.</summary>
/// <param name="Dados">Pessoa, setores e possíveis gestores.</param>
/// <param name="Form">Formulário completo.</param>
public sealed record EditarPessoaViewModel(PessoaParaEdicaoDto Dados, EditarPessoaForm Form);
