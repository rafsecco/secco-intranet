using Secco.Intranet.Web.Models.Diretorio;

namespace Secco.Intranet.Web.Demonstracao;

/// <summary>
/// Pessoas fictícias do diretório, exibidas apenas com <c>Intranet:Demo:Habilitado</c> ligado.
/// Existe para exercitar o tema enquanto o diretório organizacional real não chega (Fase 1
/// do roadmap) — não é semente de banco e nunca é gravado.
/// </summary>
internal static class DiretorioDemonstracao
{
	public static IReadOnlyList<PessoaViewModel> Pessoas() =>
	[
		new("Ana Ribeiro", "Diretora de Operações", "Diretoria", "diretoria", "ana.ribeiro@exemplo.local", "2100"),
		new("Bruno Tavares", "Analista de Infraestrutura", "Infraestrutura", "infraestrutura", "bruno.tavares@exemplo.local", "2210"),
		new("Camila Nunes", "Coordenadora de Pessoas", "Recursos Humanos", "recursos-humanos", "camila.nunes@exemplo.local", "2305"),
		new("Diego Prado", "Analista Financeiro", "Financeiro", "financeiro", "diego.prado@exemplo.local", "2412"),
		new("Elisa Moraes", "Especialista em Segurança", "Infraestrutura", "infraestrutura", "elisa.moraes@exemplo.local", "2215"),
		new("Felipe Andrade", "Analista de Benefícios", "Recursos Humanos", "recursos-humanos", "felipe.andrade@exemplo.local", "2310"),
		new("Gabriela Lopes", "Controller", "Financeiro", "financeiro", "gabriela.lopes@exemplo.local", "2405"),
		new("Henrique Salles", "Gerente de Tecnologia", "Infraestrutura", "infraestrutura", "henrique.salles@exemplo.local", "2201"),
	];
}
