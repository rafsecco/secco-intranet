using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Secco.Intranet.Web.Extensibility;

namespace Secco.Intranet.Web.Navigation;

/// <summary>
/// Catálogo dos itens extras do menu principal. Descobre os tipos uma vez, no primeiro uso
/// (application parts ∪ registros do DI, sem repetir), e a cada <see cref="Criar"/> instancia os
/// válidos no escopo da requisição. Item inválido some do menu com um aviso, uma vez por tipo.
/// </summary>
public sealed partial class CatalogoDeItensDoMenuPrincipal
{
	/// <summary>Chave reservada ao item fixo Mural.</summary>
	public const string ChaveMural = "mural";

	/// <summary>Chave reservada ao item fixo Diretório.</summary>
	public const string ChaveDiretorio = "diretorio";

	private readonly Lazy<IReadOnlyList<Type>> _tipos;
	private readonly ILogger<CatalogoDeItensDoMenuPrincipal> _logger;
	private readonly ConcurrentDictionary<Type, bool> _avisados = new();

	/// <summary>Cria o catálogo; nada é lido antes do primeiro <see cref="Criar"/>.</summary>
	/// <param name="partManager">Fonte das application parts.</param>
	/// <param name="registros">Tipos registrados pelo DI.</param>
	/// <param name="logger">Log dos avisos.</param>
	public CatalogoDeItensDoMenuPrincipal(
		ApplicationPartManager partManager,
		IEnumerable<RegistroDeItemDeMenuPrincipal> registros,
		ILogger<CatalogoDeItensDoMenuPrincipal> logger)
	{
		ArgumentNullException.ThrowIfNull(partManager);
		ArgumentNullException.ThrowIfNull(registros);
		_logger = logger;
		_tipos = new Lazy<IReadOnlyList<Type>>(() => DescobrirTipos(partManager, registros));
	}

	/// <summary>
	/// Instancia os itens no escopo dado (<see cref="ActivatorUtilities"/>) e devolve só os válidos
	/// e sem chave repetida, na ordem de <see cref="Type.FullName"/> (ordinal).
	/// </summary>
	/// <param name="escopo">Provedor do escopo da requisição.</param>
	/// <returns>Itens válidos.</returns>
	public IReadOnlyList<IItemDeMenuPrincipal> Criar(IServiceProvider escopo)
	{
		ArgumentNullException.ThrowIfNull(escopo);

		var itens = new List<IItemDeMenuPrincipal>();
		var chaves = new HashSet<string>(StringComparer.Ordinal);
		foreach (var tipo in _tipos.Value)
		{
			IItemDeMenuPrincipal item;
			string? violacao;
			try
			{
				item = (IItemDeMenuPrincipal)ActivatorUtilities.CreateInstance(escopo, tipo);
				violacao = Validar(item);
			}
#pragma warning disable CA1031 // item de terceiro nunca derruba a página
			catch (Exception ex)
#pragma warning restore CA1031
			{
				Avisar(tipo, $"falhou ao ser criado ou lido: {ex.Message}");
				continue;
			}

			if (violacao is null && !chaves.Add(item.Chave))
			{
				violacao = $"a chave '{item.Chave}' já é de outro item (vale o de menor nome de tipo)";
			}

			if (violacao is not null)
			{
				Avisar(tipo, violacao);
				continue;
			}

			itens.Add(item);
		}

		return itens;
	}

	private List<Type> DescobrirTipos(ApplicationPartManager partManager, IEnumerable<RegistroDeItemDeMenuPrincipal> registros)
	{
		try
		{
			var dasParts = partManager.ApplicationParts
				.OfType<AssemblyPart>()
				.SelectMany(p => p.Types)
				.Select(t => t.AsType())
				.Where(t => t.IsClass && !t.IsAbstract && t.IsVisible && !t.ContainsGenericParameters
					&& typeof(IItemDeMenuPrincipal).IsAssignableFrom(t));

			return dasParts
				.Concat(registros.Select(r => r.Tipo))
				.Distinct()
				.OrderBy(t => t.FullName, StringComparer.Ordinal)
				.ToList();
		}
#pragma warning disable CA1031 // uma RCL externa quebrada nunca derruba a página
		catch (Exception ex)
#pragma warning restore CA1031
		{
			_logger.LogWarning(ex, "Não foi possível descobrir os itens extras do menu principal; o menu segue sem eles.");

			return [];
		}
	}

	private static string? Validar(IItemDeMenuPrincipal item)
	{
		var chave = item.Chave;
		if (chave is null || !ChaveValida().IsMatch(chave))
		{
			return "a chave deve ter de 1 a 64 caracteres em [a-z0-9-]";
		}

		if (chave is ChaveMural or ChaveDiretorio)
		{
			return $"a chave '{chave}' é reservada aos itens fixos";
		}

		if (string.IsNullOrWhiteSpace(item.Rotulo))
		{
			return "o rótulo é obrigatório";
		}

		var url = item.Url;
		if (url is null || !url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal))
		{
			return "a url deve ser um caminho local começando com '/' e não com '//'";
		}

		return null;
	}

	private void Avisar(Type tipo, string motivo)
	{
		if (_avisados.TryAdd(tipo, true))
		{
			_logger.LogWarning("Item de menu principal {Tipo} ignorado: {Motivo}", tipo.FullName, motivo);
		}
	}

	[GeneratedRegex(@"\A[a-z0-9-]{1,64}\z", RegexOptions.CultureInvariant)]
	private static partial Regex ChaveValida();
}
