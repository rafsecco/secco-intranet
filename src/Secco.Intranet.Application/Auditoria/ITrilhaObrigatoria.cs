using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Auditoria;

/// <summary>
/// Porta da trilha <b>fail-closed</b>, para as ações cujo registro é pré-condição: hoje só a
/// exportação (ADR-0058 da plataforma, ADR-0013). Convive com a <see cref="ITrilhaDeAuditoria"/>,
/// que é fail-open de propósito: lá, a ação do usuário já aconteceu e desfazê-la por causa do
/// registro seria pior que o buraco na trilha. Aqui, o arquivo ainda não saiu, e sem registro ele
/// não sai.
/// </summary>
public interface ITrilhaObrigatoria
{
	/// <summary>
	/// Registra a ação e só devolve sucesso quando o registro foi aceito, ou quando a instalação
	/// está em Development/Testing sem LogStream configurado. Falha de infraestrutura vira
	/// <see cref="IntranetErrors.Auditoria.TrilhaIndisponivel"/>; nunca é engolida.
	/// <paramref name="registro"/> nulo é erro de programador e lança; cancelamento pedido pelo
	/// chamador propaga.
	/// </summary>
	/// <param name="registro">Ação a registrar.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	Task<Result> RegistrarAsync(RegistroDeAuditoria registro, CancellationToken cancellationToken = default);
}
