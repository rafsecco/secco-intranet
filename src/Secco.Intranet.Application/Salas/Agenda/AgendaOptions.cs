namespace Secco.Intranet.Application.Salas.Agenda;

/// <summary>
/// Configuração do espelhamento na agenda externa, seção <c>Intranet:Agenda</c>. Vazio, o espelhamento
/// fica desligado. A validação roda na subida (ver a extensão da Infrastructure).
/// </summary>
public sealed class AgendaOptions
{
	/// <summary>Chave da seção de configuração.</summary>
	public const string SectionKey = "Intranet:Agenda";

	/// <summary>Menor intervalo aceito entre ciclos, em segundos.</summary>
	public const int IntervaloMinimoEmSegundos = 5;

	/// <summary>Provedor: vazio (desligado) ou <c>Microsoft</c>, sem diferenciar maiúsculas.</summary>
	public string? Provedor { get; set; }

	/// <summary>Intervalo entre ciclos do processo em segundo plano, em segundos.</summary>
	public int IntervaloEmSegundos { get; set; } = 30;

	/// <summary>Configuração do provedor Microsoft 365.</summary>
	public MicrosoftAgendaOptions Microsoft { get; set; } = new();
}

/// <summary>Configuração do provedor Microsoft 365, seção <c>Intranet:Agenda:Microsoft</c>.</summary>
public sealed class MicrosoftAgendaOptions
{
	/// <summary>Identificador (GUID) da organização.</summary>
	public string? TenantId { get; set; }

	/// <summary>Identificador (GUID) do aplicativo registrado.</summary>
	public string? ClientId { get; set; }

	/// <summary>Segredo do aplicativo. Só por variável de ambiente ou cofre, nunca em arquivo versionado.</summary>
	public string? ClientSecret { get; set; }

	/// <summary>E-mail da caixa de serviço que organiza os eventos.</summary>
	public string? CaixaOrganizadora { get; set; }
}
