using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Pessoa que pode ser responsável por um vencimento do setor.</summary>
/// <param name="Id">Identificador do usuário no SecureGate.</param>
/// <param name="Nome">Nome de exibição; sem ele, o e-mail; sem e-mail, o identificador.</param>
public sealed record ResponsavelPossivelDto(Guid Id, string Nome);

/// <summary>
/// Lista quem pode ser responsável por um vencimento do setor: os usuários com a Role
/// <c>{slug}-admin</c> ou <c>{slug}-user</c>. As Roles vêm do cadastro do tenant e os nomes da
/// mesma fonte que o Diretório usa.
/// </summary>
public sealed class ListarResponsaveisPossiveisHandler(IDiretorioDeUsuarios diretorio, IUsuariosParaDiretorio usuarios)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="setorSlug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlyList<ResponsavelPossivelDto>> HandleAsync(string setorSlug, CancellationToken cancellationToken = default)
	{
		var roles = new[] { $"{setorSlug}-admin", $"{setorSlug}-user" };
		var doTenant = await diretorio.ListarDoTenantAtualAsync(cancellationToken).ConfigureAwait(false);

		var comAcesso = doTenant
			.Where(usuario => usuario.Roles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase)))
			.ToList();

		if (comAcesso.Count == 0)
		{
			return [];
		}

		// Nome é enfeite: se a fonte do Diretório falha, cai para o e-mail em vez de esconder a lista.
		var ativos = await usuarios.ListarAtivosAsync(cancellationToken).ConfigureAwait(false);
		var nomes = ativos.IsSuccess
			? ativos.Value
				.Where(usuario => !string.IsNullOrWhiteSpace(usuario.Nome))
				.ToDictionary(usuario => usuario.Id, usuario => usuario.Nome!)
			: [];

		return
		[
			.. comAcesso
				.Select(usuario => new ResponsavelPossivelDto(
					usuario.Id,
					nomes.GetValueOrDefault(usuario.Id)
						?? (string.IsNullOrWhiteSpace(usuario.Email) ? usuario.Id.ToString() : usuario.Email)))
				.OrderBy(responsavel => responsavel.Nome, StringComparer.CurrentCultureIgnoreCase),
		];
	}
}
