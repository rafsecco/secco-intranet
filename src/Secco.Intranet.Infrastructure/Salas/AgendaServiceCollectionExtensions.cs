using System.Net.Mail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Secco.Intranet.Application.Salas;
using Secco.Intranet.Application.Salas.Agenda;

namespace Secco.Intranet.Infrastructure.Salas;

/// <summary>Registro do espelhamento das reservas na agenda externa.</summary>
public static class AgendaServiceCollectionExtensions
{
	private const string Microsoft = "Microsoft";

	private const string ChaveDoProvedor = AgendaOptions.SectionKey + ":Provedor";
	private const string ChaveDoIntervalo = AgendaOptions.SectionKey + ":IntervaloEmSegundos";
	private const string ChaveDoTenantId = AgendaOptions.SectionKey + ":Microsoft:TenantId";
	private const string ChaveDoClientId = AgendaOptions.SectionKey + ":Microsoft:ClientId";
	private const string ChaveDoSegredo = AgendaOptions.SectionKey + ":Microsoft:ClientSecret";
	private const string ChaveDaCaixa = AgendaOptions.SectionKey + ":Microsoft:CaixaOrganizadora";

	/// <summary>
	/// Registra o espelhamento: <c>Intranet:Agenda:Provedor</c> vazio deixa tudo desligado (sem processo
	/// em segundo plano e sem estado nas telas); <c>Microsoft</c> (sem distinguir caixa nem espaços nas
	/// pontas) liga o processo e exige as demais chaves válidas. Qualquer outro valor, ou chave inválida,
	/// derruba a subida com uma mensagem que cita a chave e nunca o valor do segredo.
	/// </summary>
	/// <param name="services">Coleção de serviços da aplicação.</param>
	/// <param name="configuration">Configuração da aplicação.</param>
	public static IServiceCollection AddAgendaExterna(this IServiceCollection services, IConfiguration configuration)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(configuration);

		services.AddOptions<AgendaOptions>()
			.Configure(options => configuration.GetSection(AgendaOptions.SectionKey).Bind(options))
			.ValidateOnStart();
		services.AddSingleton<IValidateOptions<AgendaOptions>, ValidadorDaAgenda>();
		services.AddSingleton(provider => provider.GetRequiredService<IOptions<AgendaOptions>>().Value);

		// O processo em segundo plano só existe se for registrado, então a decisão sai já daqui.
		if (string.Equals(configuration[ChaveDoProvedor]?.Trim(), Microsoft, StringComparison.OrdinalIgnoreCase))
		{
			services.RemoveAll<IEspelhamentoLigado>();
			services.AddSingleton<IEspelhamentoLigado, EspelhamentoLigado>();
			services.AddSingleton<IAgendaExterna, AgendaMicrosoftProvisoria>();
			services.AddHostedService<EspelhamentoWorker>();
		}

		return services;
	}

	/// <summary>Valida a configuração com uma mensagem por chave inválida, sem nunca citar o valor do segredo.</summary>
	internal sealed class ValidadorDaAgenda : IValidateOptions<AgendaOptions>
	{
		/// <inheritdoc />
		public ValidateOptionsResult Validate(string? name, AgendaOptions options)
		{
			var provedor = options.Provedor?.Trim();

			if (string.IsNullOrEmpty(provedor))
			{
				return ValidateOptionsResult.Success;
			}

			if (!string.Equals(provedor, Microsoft, StringComparison.OrdinalIgnoreCase))
			{
				return ValidateOptionsResult.Fail($"{ChaveDoProvedor} aceita vazio ou {Microsoft}.");
			}

			var erros = new List<string>();

			if (options.IntervaloEmSegundos < AgendaOptions.IntervaloMinimoEmSegundos)
			{
				erros.Add($"{ChaveDoIntervalo} precisa ser de pelo menos {AgendaOptions.IntervaloMinimoEmSegundos} segundos.");
			}

			if (!EhGuid(options.Microsoft.TenantId))
			{
				erros.Add($"{ChaveDoTenantId} precisa ser um GUID.");
			}

			if (!EhGuid(options.Microsoft.ClientId))
			{
				erros.Add($"{ChaveDoClientId} precisa ser um GUID.");
			}

			if (string.IsNullOrWhiteSpace(options.Microsoft.ClientSecret))
			{
				erros.Add($"{ChaveDoSegredo} é obrigatória (informe por variável de ambiente ou cofre).");
			}

			if (!EhEmail(options.Microsoft.CaixaOrganizadora))
			{
				erros.Add($"{ChaveDaCaixa} precisa ser um e-mail.");
			}

			return erros.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(erros);
		}

		private static bool EhGuid(string? valor) =>
			Guid.TryParse(valor?.Trim(), out var guid) && guid != Guid.Empty;

		private static bool EhEmail(string? valor) =>
			!string.IsNullOrWhiteSpace(valor)
			&& MailAddress.TryCreate(valor.Trim(), out var endereco)
			&& string.Equals(endereco.Address, valor.Trim(), StringComparison.Ordinal)
			&& endereco.Host.Contains('.', StringComparison.Ordinal);
	}
}
