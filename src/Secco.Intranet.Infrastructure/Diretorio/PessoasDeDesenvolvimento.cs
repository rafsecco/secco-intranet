namespace Secco.Intranet.Infrastructure.Diretorio;

/// <summary>
/// Colaboradores fictícios de desenvolvimento (ADR-0019). Ids fixos e determinísticos: o seeder
/// grava os perfis com estes ids, e o adaptador de DEV os devolve como usuários do tenant.
/// Substitui a demonstração estática: só existe em <c>Development</c> sem SecureGate.
/// </summary>
internal static class PessoasDeDesenvolvimento
{
	public sealed record Pessoa(
		Guid Id, string Email, string Nome, string Cargo, string Ramal, string SetorSlug, Guid? GestorId);

	public static readonly Guid AnaId = Guid.Parse("0dee0000-0000-7000-8000-000000000001");
	public static readonly Guid HenriqueId = Guid.Parse("0dee0000-0000-7000-8000-000000000008");
	public static readonly Guid CamilaId = Guid.Parse("0dee0000-0000-7000-8000-000000000003");
	public static readonly Guid GabrielaId = Guid.Parse("0dee0000-0000-7000-8000-000000000007");

	public static readonly IReadOnlyList<Pessoa> Todas =
	[
		new(AnaId, "ana.ribeiro@exemplo.local", "Ana Ribeiro", "Diretora de Operações", "2100", "diretoria", null),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000002"), "bruno.tavares@exemplo.local", "Bruno Tavares", "Analista de Infraestrutura", "2210", "infraestrutura", HenriqueId),
		new(CamilaId, "camila.nunes@exemplo.local", "Camila Nunes", "Coordenadora de Pessoas", "2305", "recursos-humanos", AnaId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000004"), "diego.prado@exemplo.local", "Diego Prado", "Analista Financeiro", "2412", "financeiro", GabrielaId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000005"), "elisa.moraes@exemplo.local", "Elisa Moraes", "Especialista em Segurança", "2215", "infraestrutura", HenriqueId),
		new(Guid.Parse("0dee0000-0000-7000-8000-000000000006"), "felipe.andrade@exemplo.local", "Felipe Andrade", "Analista de Benefícios", "2310", "recursos-humanos", CamilaId),
		new(GabrielaId, "gabriela.lopes@exemplo.local", "Gabriela Lopes", "Controller", "2405", "financeiro", AnaId),
		new(HenriqueId, "henrique.salles@exemplo.local", "Henrique Salles", "Gerente de Tecnologia", "2201", "infraestrutura", AnaId),
	];
}
