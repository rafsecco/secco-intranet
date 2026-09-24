using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>Perfil de uma pessoa com o gestor e a equipe direta.</summary>
/// <param name="Pessoa">A pessoa.</param>
/// <param name="Gestor">O gestor, quando é um usuário ativo.</param>
/// <param name="Equipe">Quem reporta diretamente a ela, ordenado por nome.</param>
public sealed record PessoaDetalheDto(PessoaDto Pessoa, PessoaDto? Gestor, IReadOnlyList<PessoaDto> Equipe);

/// <summary>Detalhe de uma pessoa do diretório.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ObterPessoaHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Id da pessoa no SecureGate.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoaDetalheDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoaDetalheDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var pessoa = pessoas.FirstOrDefault(candidata => candidata.UsuarioId == usuarioId);

		if (pessoa is null)
		{
			return Result.Failure<PessoaDetalheDto>(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		var gestor = pessoa.GestorUsuarioId is { } gestorId
			? pessoas.FirstOrDefault(candidata => candidata.UsuarioId == gestorId)
			: null;

		IReadOnlyList<PessoaDto> equipe =
			[.. pessoas.Where(candidata => candidata.GestorUsuarioId == usuarioId).OrderBy(candidata => candidata.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoaDetalheDto(pessoa, gestor, equipe);
	}
}
