using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Diretorio;

namespace Secco.Intranet.Application.Diretorio;

/// <summary>
/// Junta identidade (usuários ativos do SecureGate) com o perfil complementar local e o setor.
/// Função pura, sem I/O — o organograma e as telas partem do mesmo resultado.
/// </summary>
public static class MontadorDePessoas
{
	/// <summary>
	/// Monta uma <see cref="PessoaDto"/> por usuário ativo. Perfil de quem não está na lista (usuário
	/// desativado ou removido) é ignorado.
	/// </summary>
	/// <param name="usuarios">Usuários ativos.</param>
	/// <param name="perfis">Todos os perfis locais.</param>
	/// <param name="setores">Todos os setores, ativos ou não (para nomear a lotação).</param>
	public static IReadOnlyList<PessoaDto> Montar(
		IReadOnlyList<UsuarioParaDiretorio> usuarios,
		IReadOnlyList<PerfilColaborador> perfis,
		IReadOnlyList<SetorDto> setores)
	{
		ArgumentNullException.ThrowIfNull(usuarios);
		ArgumentNullException.ThrowIfNull(perfis);
		ArgumentNullException.ThrowIfNull(setores);

		var usuarioPorId = usuarios.GroupBy(usuario => usuario.Id).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var perfilPorUsuario = perfis.GroupBy(perfil => perfil.UsuarioId).ToDictionary(grupo => grupo.Key, grupo => grupo.First());
		var setorPorId = setores.GroupBy(setor => setor.Id).ToDictionary(grupo => grupo.Key, grupo => grupo.First());

		string NomeDe(UsuarioParaDiretorio usuario) =>
			perfilPorUsuario.TryGetValue(usuario.Id, out var perfil) && !string.IsNullOrWhiteSpace(perfil.NomeExibicao)
				? perfil.NomeExibicao
				: string.IsNullOrWhiteSpace(usuario.Email) ? usuario.Id.ToString() : usuario.Email;

		return
		[
			.. usuarioPorId.Values.Select(usuario =>
			{
				perfilPorUsuario.TryGetValue(usuario.Id, out var perfil);

				SetorDto? setor = perfil?.SetorId is { } setorId && setorPorId.TryGetValue(setorId, out var encontrado)
					? encontrado
					: null;

				string? gestorNome = null;
				var gestorInativo = false;

				if (perfil?.GestorUsuarioId is { } gestorId)
				{
					if (usuarioPorId.TryGetValue(gestorId, out var gestor))
					{
						gestorNome = NomeDe(gestor);
					}
					else
					{
						gestorInativo = true;
					}
				}

				return new PessoaDto(
					usuario.Id,
					usuario.Email,
					NomeDe(usuario),
					perfil?.Cargo,
					perfil?.Ramal,
					perfil?.Sobre,
					setor?.Id,
					setor?.Nome,
					setor?.Slug,
					setor?.Ativo ?? false,
					perfil?.GestorUsuarioId,
					gestorNome,
					gestorInativo,
					perfil is not null);
			}),
		];
	}
}
