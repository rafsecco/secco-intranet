using Secco.SharedKernel.Authorization;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Tenants.Acesso;

/// <summary>
/// As permissões marcadas na tela de um perfil. O catálogo da plataforma (ADR-0039) é quem diz se
/// a permissão existe; aqui só o formato <c>recurso:acao</c>, o limite e as duplicadas.
/// </summary>
public static class ListaDePermissoes
{
	/// <summary>Máximo de permissões por perfil.</summary>
	public const int Maximo = 200;

	/// <summary>Normaliza a lista: apara, ignora vazias, descarta duplicadas mantendo a ordem.</summary>
	/// <param name="permissoes">Permissões enviadas pelo formulário.</param>
	public static Result<IReadOnlyList<string>> Ler(IEnumerable<string>? permissoes)
	{
		var linhas = (permissoes ?? [])
			.Select(permissao => permissao?.Trim() ?? string.Empty)
			.Where(permissao => permissao.Length > 0)
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
