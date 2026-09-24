using Secco.Intranet.Application.Diretorio;

namespace Secco.Intranet.Web.Models.Diretorio;

/// <summary>Modelo da grade de pessoas.</summary>
/// <param name="Tela">Página de pessoas e setores para o filtro.</param>
/// <param name="Busca">Termo buscado, para repopular o campo.</param>
/// <param name="SetorSlug">Setor filtrado, para repopular o seletor.</param>
public sealed record DiretorioViewModel(PessoasDaTelaDto Tela, string? Busca, string? SetorSlug);

/// <summary>Modelo da página de uma pessoa.</summary>
/// <param name="Detalhe">Pessoa, gestor e equipe.</param>
/// <param name="PodeEditar">Se quem vê pode editar (o dono do perfil ou um admin).</param>
/// <param name="EditarUrl">Para onde o botão de editar leva.</param>
public sealed record PessoaViewModel(PessoaDetalheDto Detalhe, bool PodeEditar, string? EditarUrl);
