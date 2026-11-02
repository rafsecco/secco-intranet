using Secco.Intranet.Domain.Bibliotecas;

namespace Secco.Intranet.Application.Bibliotecas;

/// <summary>Representação de leitura de uma biblioteca — a entidade nunca cruza a borda HTTP.</summary>
/// <param name="Id">Identificador.</param>
/// <param name="Nome">Nome de exibição.</param>
/// <param name="Slug">Identificador curto, imutável (base das permissões e da URL).</param>
/// <param name="Icone">Classe do Bootstrap Icons exibida na listagem.</param>
/// <param name="Ativo">Se a biblioteca está ativa.</param>
/// <param name="CreatedAt">Momento da criação.</param>
public sealed record BibliotecaDto(
	Guid Id,
	string Nome,
	string Slug,
	string Icone,
	bool Ativo,
	DateTimeOffset CreatedAt)
{
	/// <summary>Projeta a entidade para o DTO.</summary>
	/// <param name="entity">Biblioteca de origem.</param>
	public static BibliotecaDto FromEntity(Biblioteca entity)
	{
		ArgumentNullException.ThrowIfNull(entity);

		return new(entity.Id, entity.Nome, entity.Slug, entity.Icone, entity.Ativo, entity.CreatedAt);
	}
}
