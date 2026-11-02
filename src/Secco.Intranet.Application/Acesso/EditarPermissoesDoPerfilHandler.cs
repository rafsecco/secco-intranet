using Secco.Intranet.Application.Auditoria;
using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Acesso;

/// <summary>Edita as permissões de um perfil (ADR-0021) — substitui a lista inteira.</summary>
/// <param name="gestao">Porta de gestão de acesso.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarPermissoesDoPerfilHandler(IGestaoDeAcesso gestao, ITrilhaDeAuditoria trilha)
{
	private const string PrefixoDeBiblioteca = "biblioteca-";
	private const string SufixoDeEscrita = ":write";

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="perfil">Nome do perfil.</param>
	/// <param name="permissoes">Lista final de permissões, no formato canônico.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(
		string perfil, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default)
	{
		if (permissoes.Any(permissao => !SeccoPermissions.IsValid(permissao)))
		{
			return Result.Failure(IntranetErrors.Acesso.PermissaoInvalida);
		}

		// Quem escreve numa biblioteca precisa lê-la: sem o read, a biblioteca some para ele (404 indistinto).
		var finais = ComLeituraDasBibliotecas(permissoes);

		var definido = await gestao.DefinirPermissoesDoPerfilAsync(perfil, finais, cancellationToken).ConfigureAwait(false);

		if (definido.IsFailure)
		{
			return definido;
		}

		await AuditoriaDeAcesso.PermissoesAsync(trilha, perfil, finais, cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}

	/// <summary>
	/// Acrescenta <c>biblioteca-{x}:read</c> a todo <c>biblioteca-{x}:write</c> que não o traz.
	/// Só a família da biblioteca é tocada; o resto da lista segue como veio.
	/// </summary>
	private static IReadOnlyCollection<string> ComLeituraDasBibliotecas(IReadOnlyCollection<string> permissoes)
	{
		var finais = new List<string>(permissoes);
		var presentes = new HashSet<string>(permissoes, StringComparer.Ordinal);

		foreach (var permissao in permissoes)
		{
			if (!SeccoPermissions.MatchesFamily(IntranetPermissoes.Biblioteca.FamiliaWrite, permissao))
			{
				continue;
			}

			var slug = permissao[PrefixoDeBiblioteca.Length..^SufixoDeEscrita.Length];
			var leitura = IntranetPermissoes.Biblioteca.Read(slug);

			if (presentes.Add(leitura))
			{
				finais.Add(leitura);
			}
		}

		return finais;
	}
}
