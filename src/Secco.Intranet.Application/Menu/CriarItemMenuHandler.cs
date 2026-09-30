using System.Text.RegularExpressions;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Menu;

/// <summary>Cria um item na árvore de um setor.</summary>
/// <param name="repository">Persistência da árvore.</param>
public sealed class CriarItemMenuHandler(IItemMenuRepository repository)
{
	// Limites = HasMaxLength de ItemMenuConfiguration (Task 2). Validar aqui é o que
	// transforma "estourou a coluna" (500) em Result de validação (ADR-0004).
	private const int LimiteNome = 256;
	private const int LimiteSlug = 128;
	private const int LimiteRota = 512;
	private const int LimiteIcone = 64;

	private static readonly Regex FormatoDoSlug = new("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.Compiled);

	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados do novo item.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<ItemMenuDto>> HandleAsync(CriarItemMenuCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		if (!Enum.IsDefined(command.Tipo) || command.Tipo == TipoDeItemMenu.Setor)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.TipoInvalido);
		}

		if (string.IsNullOrWhiteSpace(command.Nome))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.NomeRequired);
		}

		if (command.Nome.Trim().Length > LimiteNome)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O nome", LimiteNome));
		}

		if (string.IsNullOrWhiteSpace(command.Slug))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugRequired);
		}

		var slugNormalizado = command.Slug.Trim().ToLowerInvariant();

		if (slugNormalizado.Length > LimiteSlug)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O identificador", LimiteSlug));
		}

		if (!FormatoDoSlug.IsMatch(slugNormalizado))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugInvalido);
		}

		if (!ItemMenu.IconeEhValido(command.Icone))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.IconeInvalido);
		}

		if (command.Icone?.Trim().Length > LimiteIcone)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("O ícone", LimiteIcone));
		}

		// Rota só tem papel em Personalizado; nos outros tipos é descartada, não validada.
		var rota = command.Tipo == TipoDeItemMenu.Personalizado ? command.Rota?.Trim() : null;

		if (!string.IsNullOrEmpty(rota))
		{
			if (rota.Length > LimiteRota)
			{
				return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.CampoMuitoLongo("A rota", LimiteRota));
			}

			if (!RotaEhValida(rota))
			{
				return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.RotaInvalida);
			}
		}

		var arvore = await repository.ListarPorSetorAsync(command.SetorId, cancellationToken).ConfigureAwait(false);
		var pai = arvore.FirstOrDefault(item => item.Id == command.ParentId);

		if (pai is null)
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.PaiInvalido);
		}

		if (arvore.Any(item => item.ParentId == command.ParentId
			&& string.Equals(item.Slug, slugNormalizado, StringComparison.Ordinal)))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.SlugJaExisteEntreIrmaos);
		}

		if ((command.Tipo == TipoDeItemMenu.Documentos || command.Tipo == TipoDeItemMenu.Avisos)
			&& await repository.ExisteTipoAsync(command.SetorId, command.Tipo, cancellationToken).ConfigureAwait(false))
		{
			return Result.Failure<ItemMenuDto>(IntranetErrors.Menu.TipoJaExiste);
		}

		var proximaOrdem = arvore.Where(item => item.ParentId == command.ParentId).Select(item => item.Ordem).DefaultIfEmpty(-1).Max() + 1;

		var item = new ItemMenu(
			command.SetorId, command.ParentId, command.Nome.Trim(), slugNormalizado, command.Tipo,
			rota, command.Icone, proximaOrdem);

		await repository.AddAsync(item, cancellationToken).ConfigureAwait(false);

		return ItemMenuDto.FromEntity(item);
	}

	/// <summary>
	/// Rota é destino de <c>Redirect</c> (Task 9): caminho local começando com uma barra só
	/// (<c>//host</c> seria outro domínio), ou URL absoluta http/https. Qualquer outro esquema
	/// (<c>javascript:</c>, <c>data:</c>, <c>ftp:</c>) é recusado.
	/// </summary>
	private static bool RotaEhValida(string rota) =>
		(rota.StartsWith('/') && !rota.StartsWith("//", StringComparison.Ordinal) && !rota.StartsWith("/\\", StringComparison.Ordinal))
		|| (Uri.TryCreate(rota, UriKind.Absolute, out var uri)
			&& (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));
}
