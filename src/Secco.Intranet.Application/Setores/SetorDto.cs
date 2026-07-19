using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Application.Setores;

/// <summary>Representação de leitura de um setor — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Slug">Identificador curto (base das Roles no SecureGate).</param>
/// <param name="Fixo">Se o setor é fixo do sistema (não pode ser desativado/excluído).</param>
/// <param name="Ativo">Se o setor está ativo.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record SetorDto(Guid Id, string Nome, string Slug, bool Fixo, bool Ativo, DateTimeOffset CreatedAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	public static SetorDto FromEntity(Setor entity) =>
		new(entity.Id, entity.Nome, entity.Slug, entity.Fixo, entity.Ativo, entity.CreatedAt);
}
