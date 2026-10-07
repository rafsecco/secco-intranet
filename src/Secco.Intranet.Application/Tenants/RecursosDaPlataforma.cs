namespace Secco.Intranet.Application.Tenants;

/// <summary>Recurso da plataforma que a Intranet liga para um tenant administrado.</summary>
public enum RecursoDaPlataforma
{
	/// <summary>Usuários e perfis. Sem banco por tenant (identidade é dado de plataforma, ADR-0022).</summary>
	SecureGate,

	/// <summary>Log e trilha de auditoria. Banco por tenant.</summary>
	LogStream,

	/// <summary>Notificações. Banco por tenant.</summary>
	NotificationHub,
}

/// <summary>
/// Lista fechada dos recursos e de como cada um aparece na rota e no catálogo. O texto que vem da
/// rota é comparado com esta lista e nunca repassado à plataforma: a API aceita qualquer nome de
/// produto, e escolher um arbitrário a partir da Intranet é superfície que ninguém pediu.
/// </summary>
public static class RecursosDaPlataforma
{
	/// <summary>Todos, na ordem em que a tela os mostra.</summary>
	public static IReadOnlyList<RecursoDaPlataforma> Todos { get; } =
		[RecursoDaPlataforma.SecureGate, RecursoDaPlataforma.LogStream, RecursoDaPlataforma.NotificationHub];

	/// <summary>Lê o segmento de rota, sem diferenciar caixa e sem aparar.</summary>
	/// <param name="valor">Segmento recebido.</param>
	/// <param name="recurso">Recurso reconhecido.</param>
	/// <returns><c>false</c> para qualquer valor fora da lista, inclusive números.</returns>
	public static bool TentarLer(string? valor, out RecursoDaPlataforma recurso)
	{
		foreach (var candidato in Todos)
		{
			if (string.Equals(valor, Rota(candidato), StringComparison.OrdinalIgnoreCase))
			{
				recurso = candidato;

				return true;
			}
		}

		recurso = default;

		return false;
	}

	/// <summary>Segmento de rota do recurso.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string Rota(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => "securegate",
		RecursoDaPlataforma.LogStream => "logstream",
		RecursoDaPlataforma.NotificationHub => "notificationhub",
		_ => throw new ArgumentOutOfRangeException(nameof(recurso)),
	};

	/// <summary>Nome do produto no catálogo do SecureGate, ou <c>null</c> se o recurso não tem banco.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string? Produto(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => null,
		_ => Rota(recurso),
	};

	/// <summary>Nome de exibição.</summary>
	/// <param name="recurso">Recurso.</param>
	public static string Nome(RecursoDaPlataforma recurso) => recurso switch
	{
		RecursoDaPlataforma.SecureGate => "SecureGate",
		RecursoDaPlataforma.LogStream => "LogStream",
		RecursoDaPlataforma.NotificationHub => "NotificationHub",
		_ => throw new ArgumentOutOfRangeException(nameof(recurso)),
	};
}
