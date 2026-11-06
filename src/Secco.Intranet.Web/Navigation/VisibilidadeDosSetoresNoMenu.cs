using System.Security.Claims;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;

namespace Secco.Intranet.Web.Navigation;

/// <summary>Setores que entram no menu do usuário e o que ele pode em cada um.</summary>
/// <param name="Setores">Setores que entram na montagem do menu.</param>
/// <param name="ComEscrita">Setores com item restrito em que o usuário escreve.</param>
/// <param name="SemLeitura">Setores presentes em <paramref name="Setores"/> só por causa da escrita.</param>
public sealed record VisibilidadeDosSetores(
	IReadOnlyList<SetorDto> Setores, IReadOnlySet<Guid> ComEscrita, IReadOnlySet<Guid> SemLeitura);

/// <summary>
/// Resolve as permissões de setor que a montagem do menu precisa. A escrita só é consultada nos
/// setores com item restrito: o menu roda em toda página e o caso comum não deve custar nada.
/// </summary>
public static class VisibilidadeDosSetoresNoMenu
{
	/// <summary>Executa a resolução. Falha ao consultar nunca derruba o menu, que está no layout de toda página.</summary>
	/// <param name="permissoes">Permissões de setor do usuário.</param>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="ativos">Setores ativos candidatos ao menu.</param>
	/// <param name="arvores">Itens ativos da árvore de cada setor; nulo se não foi possível carregar.</param>
	/// <param name="modoAberto">Modo aberto de DEV local: tudo liberado, sem consultar permissão.</param>
	/// <param name="logger">Log de diagnóstico.</param>
	/// <param name="cancellationToken">Cancelamento da requisição.</param>
	public static async Task<VisibilidadeDosSetores> ResolverAsync(
		IPermissoesDeSetor permissoes,
		ClaimsPrincipal usuario,
		IReadOnlyList<SetorDto> ativos,
		IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? arvores,
		bool modoAberto,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(permissoes);
		ArgumentNullException.ThrowIfNull(ativos);

		if (ativos.Count == 0)
		{
			return Vazia;
		}

		var comItemRestrito = ativos.Where(setor => TemItemRestrito(setor, arvores)).ToList();

		if (modoAberto)
		{
			return new VisibilidadeDosSetores(ativos, comItemRestrito.Select(setor => setor.Id).ToHashSet(), new HashSet<Guid>());
		}

		IReadOnlySet<string> leitura;

		try
		{
			leitura = await permissoes
				.SlugsComPermissaoAsync(usuario, "read", [.. ativos.Select(setor => setor.Slug)], cancellationToken)
				.ConfigureAwait(false);
		}
#pragma warning disable CA1031 // O menu está no layout: uma falha aqui derrubaria até a própria página de erro.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			logger.LogWarning(exception, "Não foi possível consultar a leitura dos setores do menu; exibindo apenas os itens fixos.");
			return Vazia;
		}

		var escrita = await EscritaAsync(permissoes, usuario, comItemRestrito, logger, cancellationToken).ConfigureAwait(false);
		var visiveis = ativos.Where(setor => leitura.Contains(setor.Slug) || escrita.Contains(setor.Slug)).ToList();

		return new VisibilidadeDosSetores(
			visiveis,
			comItemRestrito.Where(setor => escrita.Contains(setor.Slug)).Select(setor => setor.Id).ToHashSet(),
			visiveis.Where(setor => !leitura.Contains(setor.Slug)).Select(setor => setor.Id).ToHashSet());
	}

	private static VisibilidadeDosSetores Vazia => new([], new HashSet<Guid>(), new HashSet<Guid>());

	private static bool TemItemRestrito(SetorDto setor, IReadOnlyDictionary<Guid, IReadOnlyList<ItemMenuDto>>? arvores) =>
		arvores is not null
		&& arvores.TryGetValue(setor.Id, out var itens)
		&& itens.Any(item => item.NivelDeAcesso == NivelDeAcessoItemMenu.Escrita);

	private static async Task<IReadOnlySet<string>> EscritaAsync(
		IPermissoesDeSetor permissoes,
		ClaimsPrincipal usuario,
		List<SetorDto> comItemRestrito,
		ILogger logger,
		CancellationToken cancellationToken)
	{
		if (comItemRestrito.Count == 0)
		{
			return new HashSet<string>();
		}

		try
		{
			return await permissoes
				.SlugsComPermissaoAsync(usuario, "write", [.. comItemRestrito.Select(setor => setor.Slug)], cancellationToken)
				.ConfigureAwait(false);
		}
#pragma warning disable CA1031 // Mesmo motivo da leitura; sem saber a escrita, o item restrito fica escondido.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			logger.LogWarning(exception, "Não foi possível consultar a escrita dos setores do menu; os itens restritos ficam escondidos.");
			return new HashSet<string>();
		}
	}
}
