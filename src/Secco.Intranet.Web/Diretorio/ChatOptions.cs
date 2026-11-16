namespace Secco.Intranet.Web.Diretorio;

/// <summary>Provedor de chat que o botão do Diretório abre.</summary>
public enum ProvedorDeChat
{
	/// <summary>Chat desligado: nenhum botão.</summary>
	Nenhum = 0,

	/// <summary>Microsoft Teams.</summary>
	Teams = 1,
}

/// <summary>Configuração do link de chat do Diretório, seção <c>Intranet:Chat</c>.</summary>
public sealed class ChatOptions
{
	/// <summary>Seção da configuração.</summary>
	public const string SectionKey = "Intranet:Chat";

	/// <summary>Provedor, lido de texto na configuração; vazio vale <see cref="ProvedorDeChat.Nenhum"/>.</summary>
	public ProvedorDeChat Provedor { get; set; }

	/// <summary>Tenant do Microsoft Entra ID, opcional; acrescentado ao endereço quando informado.</summary>
	public Guid? TenantId { get; set; }
}
