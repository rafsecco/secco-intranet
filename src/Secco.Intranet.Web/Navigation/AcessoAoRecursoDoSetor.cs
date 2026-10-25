using System.Security.Claims;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Application.Setores;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Pagination;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Checagens de acesso a um recurso embutido de um setor (documentos, avisos, vencimentos):
/// ler, escrever e saber se o recurso está ligado na árvore de menu. Compartilhado pelos
/// controllers de página, para que todos respondam igual a "não posso" e "não existe".
/// </summary>
/// <param name="getSetorHandler">Leitura do setor pelo slug.</param>
/// <param name="resolverCaminho">Resolução de caminho na árvore de itens de menu do setor.</param>
/// <param name="searchSetores">Busca de setores, para saber o universo de slugs a checar.</param>
/// <param name="permissoesDeSetor">Em quais setores o usuário tem leitura/escrita (ADR-0021).</param>
/// <param name="configuration">Configuração do host, para saber se a autenticação está ativa.</param>
/// <param name="environment">Ambiente de hospedagem — o modo aberto só vale em Development (ADR-0020).</param>
public sealed class AcessoAoRecursoDoSetor(
	GetSetorBySlugHandler getSetorHandler,
	ResolverCaminhoDeMenuHandler resolverCaminho,
	SearchSetoresHandler searchSetores,
	IPermissoesDeSetor permissoesDeSetor,
	IConfiguration configuration,
	IWebHostEnvironment environment)
{
	private const int LimiteSetoresConsultados = 200;

	/// <summary>Se o modo aberto de DEV local está ativo (Development sem autenticação configurada).</summary>
	public bool ModoAbertoDeDev => AcessoAdministrativo.ModoAbertoDeDev(environment, configuration);

	/// <summary>
	/// Ler exige a permissão de leitura do setor (ADR-0021). Só em Development sem autenticação
	/// configurada (modo aberto de DEV local) não há permissão a resolver — consistente com o menu,
	/// que nesse modo mostra todo setor. Em Testing e em qualquer outro ambiente, o caminho de
	/// permissão de verdade roda sempre, para nunca ficar aberto por engano (ADR-0020).
	/// </summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> PodeLerAsync(ClaimsPrincipal usuario, string slug, CancellationToken cancellationToken)
	{
		if (ModoAbertoDeDev)
		{
			return true;
		}

		var slugsComLeitura = await permissoesDeSetor
			.SlugsComPermissaoAsync(usuario, "read", [slug], cancellationToken)
			.ConfigureAwait(false);

		return slugsComLeitura.Contains(slug);
	}

	/// <summary>
	/// Escrever exige a permissão de escrita do setor (ADR-0021). Mesmo bypass de
	/// <see cref="PodeLerAsync"/>: só no modo aberto de DEV local a checagem é dispensada.
	/// </summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<bool> PodeEscreverAsync(ClaimsPrincipal usuario, string slug, CancellationToken cancellationToken)
	{
		if (ModoAbertoDeDev)
		{
			return true;
		}

		var slugsComEscrita = await permissoesDeSetor
			.SlugsComPermissaoAsync(usuario, "write", [slug], cancellationToken)
			.ConfigureAwait(false);

		return slugsComEscrita.Contains(slug);
	}

	/// <summary>
	/// Setor e posição na árvore do item de um tipo embutido. Nulo quando o setor não existe
	/// ou está inativo, ou quando o recurso está desligado (item ausente, ou ele/um ancestral
	/// desativado) — quem chama responde 404 nos três casos.
	/// </summary>
	/// <param name="slug">Slug do setor.</param>
	/// <param name="tipo">Tipo embutido do item de menu.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<(SetorDto Setor, ResultadoDaResolucao Resolucao)?> ResolverTipoAsync(
		string slug, TipoDeItemMenu tipo, CancellationToken cancellationToken)
	{
		var setor = await getSetorHandler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

		if (setor.IsFailure || !setor.Value.Ativo)
		{
			return null;
		}

		var caminho = await resolverCaminho.CaminhoDoTipoAsync(setor.Value.Id, tipo, cancellationToken).ConfigureAwait(false);

		if (caminho is null)
		{
			return null;
		}

		var resolvido = await resolverCaminho.HandleAsync(setor.Value.Id, caminho, cancellationToken).ConfigureAwait(false);

		return resolvido.IsFailure ? null : (setor.Value, resolvido.Value);
	}

	/// <summary>
	/// Setores em que o usuário tem escrita — vazio só no modo aberto de DEV local, quando o handler
	/// ignora o filtro por completo (<c>ExigirVinculo: false</c>). Fora do Development a lista vem
	/// sempre da permissão real.
	/// </summary>
	/// <param name="usuario">Usuário da requisição.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<IReadOnlySet<string>> SlugsComEscritaAsync(ClaimsPrincipal usuario, CancellationToken cancellationToken)
	{
		if (ModoAbertoDeDev)
		{
			return new HashSet<string>();
		}

		var setores = await searchSetores
			.HandleAsync(new SetorSearchCriteria(ApenasAtivos: true, Page: new PageRequest(1, LimiteSetoresConsultados)), cancellationToken)
			.ConfigureAwait(false);

		if (setores.IsFailure)
		{
			return new HashSet<string>();
		}

		var slugs = setores.Value.Items.Select(s => s.Slug).ToList();

		return await permissoesDeSetor.SlugsComPermissaoAsync(usuario, "write", slugs, cancellationToken).ConfigureAwait(false);
	}
}
