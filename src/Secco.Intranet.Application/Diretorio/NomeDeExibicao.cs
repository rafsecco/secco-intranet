namespace Secco.Intranet.Application.Diretorio;

/// <summary>Regras do nome de exibição, que mora no SecureGate (secco-platform#30).</summary>
public static class NomeDeExibicao
{
	/// <summary>Tamanho máximo — o mesmo que a plataforma aceita.</summary>
	public const int MaxLength = 160;

	/// <summary>Forma comparável: aparado, e vazio vira nulo (é o que a plataforma grava).</summary>
	public static string? Normalizar(string? nome) => string.IsNullOrWhiteSpace(nome) ? null : nome.Trim();
}
