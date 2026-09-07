namespace Secco.Intranet.Web;

/// <summary>
/// Liga as páginas de demonstração (seção <c>Intranet:Demo</c>). Desligado por padrão: uma
/// intranet em uso não pode servir conteúdo fictício porque alguém esqueceu de removê-lo.
/// </summary>
public sealed class DemoOptions
{
	/// <summary>Seção de configuração de onde estas opções são lidas.</summary>
	public const string SectionKey = "Intranet:Demo";

	/// <summary>Se as páginas de demonstração respondem e aparecem no menu.</summary>
	public bool Habilitado { get; set; }
}
