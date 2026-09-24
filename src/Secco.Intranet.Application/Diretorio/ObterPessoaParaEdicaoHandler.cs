using Secco.Intranet.Application.Setores;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>O que o formulário de edição do admin precisa.</summary>
/// <param name="Pessoa">A pessoa em edição, com os valores atuais.</param>
/// <param name="SetoresAtivos">Setores que podem ser escolhidos para lotação.</param>
/// <param name="GestoresPossiveis">Todas as outras pessoas ativas, por nome. Ciclo é barrado ao salvar.</param>
public sealed record PessoaParaEdicaoDto(
	PessoaDto Pessoa, IReadOnlyList<SetorDto> SetoresAtivos, IReadOnlyList<PessoaDto> GestoresPossiveis);

/// <summary>Dados para o formulário de edição completa de uma pessoa.</summary>
/// <param name="usuarios">Fonte de identidade.</param>
/// <param name="perfis">Perfis locais.</param>
/// <param name="setores">Setores do tenant.</param>
public sealed class ObterPessoaParaEdicaoHandler(
	IUsuariosParaDiretorio usuarios,
	IPerfilColaboradorRepository perfis,
	ISetorRepository setores)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Pessoa a editar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<PessoaParaEdicaoDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);

		if (ativos.IsFailure)
		{
			return Result.Failure<PessoaParaEdicaoDto>(ativos.Error);
		}

		var todosOsSetores = await ListarPessoasHandler.CarregarSetoresAsync(setores, cancellationToken).ConfigureAwait(false);
		var todosOsPerfis = await perfis.ListarTodosAsync(cancellationToken).ConfigureAwait(false);
		var pessoas = MontadorDePessoas.Montar(ativos.Value, todosOsPerfis, todosOsSetores);

		var pessoa = pessoas.FirstOrDefault(candidata => candidata.UsuarioId == usuarioId);

		if (pessoa is null)
		{
			return Result.Failure<PessoaParaEdicaoDto>(IntranetErrors.Diretorio.PessoaNaoEncontrada);
		}

		IReadOnlyList<SetorDto> ativosParaEscolha =
			[.. todosOsSetores.Where(setor => setor.Ativo).OrderBy(setor => setor.Nome, StringComparer.OrdinalIgnoreCase)];

		IReadOnlyList<PessoaDto> gestores =
			[.. pessoas.Where(candidata => candidata.UsuarioId != usuarioId).OrderBy(candidata => candidata.Nome, StringComparer.OrdinalIgnoreCase)];

		return new PessoaParaEdicaoDto(pessoa, ativosParaEscolha, gestores);
	}
}
