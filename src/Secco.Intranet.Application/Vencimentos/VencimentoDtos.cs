using Secco.Intranet.Domain.Vencimentos;

namespace Secco.Intranet.Application.Vencimentos;

/// <summary>Linha da lista de vencimentos de um setor.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="DataVencimento">Data do vencimento do ciclo atual.</param>
/// <param name="Estado">Situação em relação a hoje.</param>
/// <param name="ResponsavelId">Usuário responsável, se houver.</param>
public sealed record VencimentoResumoDto(
	Guid Id, string Titulo, DateOnly DataVencimento, EstadoDoVencimento Estado, Guid? ResponsavelId);

/// <summary>Detalhe de um vencimento, com histórico de renovações e avisos.</summary>
/// <param name="Id">Identificador do item.</param>
/// <param name="Titulo">Título de exibição.</param>
/// <param name="Descricao">Descrição livre.</param>
/// <param name="DataVencimento">Data do vencimento do ciclo atual.</param>
/// <param name="Estado">Situação em relação a hoje.</param>
/// <param name="ResponsavelId">Usuário responsável, se houver.</param>
/// <param name="Antecedencias">Marcos de aviso em dias, em ordem decrescente.</param>
/// <param name="Ciclo">Ciclo de aviso atual.</param>
/// <param name="ProximosAvisos">Marcos ainda não tratados no ciclo, com a data de cada um.</param>
/// <param name="Renovacoes">Renovações, a mais recente primeiro.</param>
/// <param name="Avisos">Avisos tratados, em todos os ciclos.</param>
public sealed record VencimentoDetalheDto(
	Guid Id,
	string Titulo,
	string? Descricao,
	DateOnly DataVencimento,
	EstadoDoVencimento Estado,
	Guid? ResponsavelId,
	IReadOnlyList<int> Antecedencias,
	int Ciclo,
	IReadOnlyList<(int Marco, DateOnly Data)> ProximosAvisos,
	IReadOnlyList<RenovacaoDeVencimento> Renovacoes,
	IReadOnlyList<AvisoDeVencimentoEnviado> Avisos);
