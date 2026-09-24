using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Perfis de setor de um usuário agrupados por setor.</summary>
/// <param name="Slug">Slug do setor.</param>
/// <param name="Papeis">Papéis que o usuário tem nesse setor (pode ter os dois).</param>
public sealed record PerfilNoSetorDto(string Slug, IReadOnlyList<PapelNoSetor> Papeis);

/// <summary>Tela de detalhe de um usuário.</summary>
/// <param name="Usuario">Detalhe do usuário.</param>
/// <param name="Setores">Perfis de setor, agrupados por setor.</param>
/// <param name="OutrosPerfis">Perfis que não são de setor (comuns e do produto).</param>
/// <param name="SlugsDeSetorDisponiveis">Setores para os quais existe Role, alimentam o seletor de setor.</param>
/// <param name="OutrosPerfisAtribuiveis">Perfis comuns e do produto, não reservados, que o usuário ainda não tem.</param>
public sealed record UsuarioTelaDto(
	UsuarioDetalheDto Usuario,
	IReadOnlyList<PerfilNoSetorDto> Setores,
	IReadOnlyList<string> OutrosPerfis,
	IReadOnlyList<string> SlugsDeSetorDisponiveis,
	IReadOnlyList<string> OutrosPerfisAtribuiveis);

/// <summary>Detalhe de um usuário com o que a tela precisa para oferecer os perfis.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
public sealed class ObterUsuarioHandler(IGestaoDeAcesso gestao)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="usuarioId">Identificador do usuário.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<UsuarioTelaDto>> HandleAsync(Guid usuarioId, CancellationToken cancellationToken = default)
	{
		var usuario = await gestao.ObterUsuarioAsync(usuarioId, cancellationToken).ConfigureAwait(false);

		if (usuario.IsFailure)
		{
			return Result.Failure<UsuarioTelaDto>(usuario.Error);
		}

		var perfis = await gestao.ListarPerfisAsync(cancellationToken).ConfigureAwait(false);

		if (perfis.IsFailure)
		{
			return Result.Failure<UsuarioTelaDto>(perfis.Error);
		}

		IReadOnlyList<PerfilNoSetorDto> setores =
		[
			.. usuario.Value.Perfis
				.Select(nome => ClassificacaoDePerfil.DoSetor(nome))
				.Where(setor => setor is not null)
				.Select(setor => setor!.Value)
				.GroupBy(setor => setor.Slug, StringComparer.OrdinalIgnoreCase)
				.OrderBy(grupo => grupo.Key, StringComparer.OrdinalIgnoreCase)
				.Select(grupo => new PerfilNoSetorDto(
					grupo.Key,
					[.. grupo.Select(setor => setor.Papel).Distinct().OrderByDescending(papel => papel)])),
		];

		IReadOnlyList<string> outros =
		[
			.. usuario.Value.Perfis
				.Where(nome => ClassificacaoDePerfil.DoSetor(nome) is null)
				.OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase),
		];

		IReadOnlyList<string> slugs =
		[
			.. perfis.Value
				.Select(perfil => ClassificacaoDePerfil.DoSetor(perfil.Nome))
				.Where(setor => setor is not null)
				.Select(setor => setor!.Value.Slug)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.OrderBy(slug => slug, StringComparer.OrdinalIgnoreCase),
		];

		IReadOnlyList<string> atribuiveis =
		[
			.. perfis.Value
				.Where(perfil => ClassificacaoDePerfil.DoSetor(perfil.Nome) is null
					&& !perfil.Reservado
					&& !ClassificacaoDePerfil.EhReservado(perfil.Nome)
					&& !usuario.Value.Perfis.Any(p => string.Equals(p, perfil.Nome, StringComparison.OrdinalIgnoreCase)))
				.Select(perfil => perfil.Nome)
				.OrderBy(nome => nome, StringComparer.OrdinalIgnoreCase),
		];

		return new UsuarioTelaDto(usuario.Value, setores, outros, slugs, atribuiveis);
	}
}
