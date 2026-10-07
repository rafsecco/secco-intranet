using Secco.Intranet.Domain.Tenants;

namespace Secco.Intranet.Application.Tenants;

/// <summary>Tenant como a plataforma o vê.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Slug">Slug.</param>
/// <param name="Ativo">Se está ativo.</param>
public sealed record TenantDaPlataformaDto(Guid Id, string Nome, string Slug, bool Ativo);

/// <summary>Tenant da plataforma com os produtos que têm banco no catálogo.</summary>
/// <param name="Id">Id no SecureGate.</param>
/// <param name="Nome">Nome.</param>
/// <param name="Slug">Slug.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="Produtos">Produtos com banco cadastrado (ex.: <c>logstream</c>).</param>
public sealed record TenantDaPlataformaDetalheDto(Guid Id, string Nome, string Slug, bool Ativo, IReadOnlyList<string> Produtos);

/// <summary>Estado de um banco do tenant, sem connection string (ADR-0020).</summary>
/// <param name="Produto">Produto.</param>
/// <param name="Responde">Se respondeu à sondagem.</param>
/// <param name="Motivo">Classificação da falha, quando não responde.</param>
public sealed record StatusDoBancoDto(string Produto, bool Responde, string? Motivo);

/// <summary>Resultado de ligar um recurso.</summary>
/// <param name="Aplicado"><c>true</c> se a plataforma criou o banco; <c>false</c> se devolveu script.</param>
/// <param name="Script">SQL para o DBA, com a senha gerada. Exibido uma vez; nunca persistido nem registrado.</param>
public sealed record ProvisionamentoDto(bool Aplicado, string? Script);

/// <summary>Situação de um recurso para um tenant.</summary>
public enum SituacaoDoRecurso
{
	/// <summary>Não ligado.</summary>
	NaoLigado,

	/// <summary>Ligado e respondendo.</summary>
	Ligado,

	/// <summary>Banco cadastrado no catálogo, mas não respondeu (ex.: script ainda não aplicado).</summary>
	LigadoSemResponder,
}

/// <summary>Um recurso no painel do tenant.</summary>
/// <param name="Recurso">Recurso.</param>
/// <param name="Situacao">Situação.</param>
/// <param name="Motivo">Motivo classificado, quando ligado sem responder.</param>
public sealed record RecursoDoTenantDto(RecursoDaPlataforma Recurso, SituacaoDoRecurso Situacao, string? Motivo);

/// <summary>Linha da lista de tenants administrados.</summary>
/// <param name="TenantId">Id no SecureGate.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Slug">Slug na plataforma.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="EncontradoNaPlataforma"><c>false</c> se o SecureGate não conhece mais o tenant.</param>
/// <param name="Origem">Criado ou adotado.</param>
/// <param name="RecursosLigados">Recursos ligados.</param>
public sealed record TenantAdministradoResumoDto(
	Guid TenantId,
	string Sistema,
	string Nome,
	string Slug,
	bool Ativo,
	bool EncontradoNaPlataforma,
	OrigemDoTenant Origem,
	IReadOnlyList<RecursoDaPlataforma> RecursosLigados);

/// <summary>Detalhe de um tenant administrado.</summary>
/// <param name="TenantId">Id no SecureGate.</param>
/// <param name="Sistema">Sistema.</param>
/// <param name="Responsavel">Responsável.</param>
/// <param name="Nome">Nome na plataforma.</param>
/// <param name="Slug">Slug na plataforma.</param>
/// <param name="Ativo">Se está ativo.</param>
/// <param name="Origem">Criado ou adotado.</param>
/// <param name="RegistradoPor">Quem registrou.</param>
/// <param name="RegistradoEm">Quando registrou.</param>
/// <param name="Recursos">Os três recursos, sempre na ordem de <see cref="RecursosDaPlataforma.Todos"/>.</param>
public sealed record TenantAdministradoDetalheDto(
	Guid TenantId,
	string Sistema,
	string Responsavel,
	string Nome,
	string Slug,
	bool Ativo,
	OrigemDoTenant Origem,
	string RegistradoPor,
	DateTimeOffset RegistradoEm,
	IReadOnlyList<RecursoDoTenantDto> Recursos);
