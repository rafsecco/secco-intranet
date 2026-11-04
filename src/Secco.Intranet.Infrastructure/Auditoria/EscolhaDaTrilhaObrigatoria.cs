using Microsoft.Extensions.Hosting;

namespace Secco.Intranet.Infrastructure.Auditoria;

/// <summary>Qual adapter da trilha obrigatória a instalação usa.</summary>
public enum ModoDaTrilhaObrigatoria
{
	/// <summary>LogStream configurado: registra lá, e só aceita o que ele aceitou.</summary>
	LogStream,

	/// <summary>Sem LogStream em Development/Testing: aceita, com aviso de que não auditou.</summary>
	Desenvolvimento,

	/// <summary>Sem LogStream em qualquer outro ambiente: recusa.</summary>
	Indisponivel,
}

/// <summary>
/// Escolha do adapter da <see cref="Application.Auditoria.ITrilhaObrigatoria"/> (ADR-0013). Decide pela
/// URL do LogStream <b>e</b> pelo nome do ambiente: um HML com a URL esquecida cai na recusa. É de
/// propósito diferente da escolha da <see cref="Application.Auditoria.ITrilhaDeAuditoria"/>, que olha só
/// a URL porque é fail-open; os dois critérios não devem ser unificados.
/// </summary>
public static class EscolhaDaTrilhaObrigatoria
{
	/// <summary>Modo para a configuração e o ambiente informados.</summary>
	/// <param name="logStreamUrl">Valor de <c>Intranet:Auditoria:LogStreamUrl</c>.</param>
	/// <param name="ambiente">Ambiente do host.</param>
	public static ModoDaTrilhaObrigatoria Para(string? logStreamUrl, IHostEnvironment ambiente)
	{
		ArgumentNullException.ThrowIfNull(ambiente);

		if (!string.IsNullOrWhiteSpace(logStreamUrl))
		{
			return ModoDaTrilhaObrigatoria.LogStream;
		}

		return EhDesenvolvimento(ambiente) ? ModoDaTrilhaObrigatoria.Desenvolvimento : ModoDaTrilhaObrigatoria.Indisponivel;
	}

	/// <summary>
	/// Development ou Testing, o mesmo critério do modo aberto do SecureGate, estendido ao Testing
	/// porque o host dos testes de integração roda nele.
	/// </summary>
	/// <param name="ambiente">Ambiente do host.</param>
	public static bool EhDesenvolvimento(IHostEnvironment ambiente)
	{
		ArgumentNullException.ThrowIfNull(ambiente);

		return ambiente.IsDevelopment() || ambiente.IsEnvironment("Testing");
	}
}
