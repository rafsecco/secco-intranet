using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// Permissões em texto livre, uma por linha — o paliativo da secco-platform#33 até o catálogo da
/// ADR-0039 ser publicado. Valida o formato <c>recurso:acao</c>; não sabe se a permissão existe.
/// </summary>
public static class ListaDePermissoes
{
	/// <summary>Máximo de permissões por perfil.</summary>
	public const int Maximo = 200;

	/// <summary>Lê o texto: apara, ignora linhas vazias, descarta duplicadas mantendo a ordem.</summary>
	/// <param name="texto">Texto do formulário.</param>
	public static Result<IReadOnlyList<string>> Ler(string? texto)
	{
		var linhas = (texto ?? string.Empty)
			.Split('\n')
			.Select(linha => linha.Trim())
			.Where(linha => linha.Length > 0)
			.Distinct(StringComparer.Ordinal)
			.ToList();

		var invalidas = linhas.Where(linha => !SeccoPermissions.IsValid(linha)).ToList();

		if (invalidas.Count > 0)
		{
			return Result.Failure<IReadOnlyList<string>>(IntranetErrors.Acesso.PermissoesInvalidas(invalidas));
		}

		return linhas.Count > Maximo
			? Result.Failure<IReadOnlyList<string>>(IntranetErrors.Acesso.PermissoesDemais)
			: Result.Success<IReadOnlyList<string>>(linhas);
	}
}
