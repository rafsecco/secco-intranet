using Secco.Intranet.Web.Models.Mural;

namespace Secco.Intranet.Web.Demonstracao;

/// <summary>
/// Conteúdo fictício do mural, exibido apenas com <c>Intranet:Demo:Habilitado</c> ligado.
/// Existe para exercitar o tema enquanto o recurso real não chega (Fase 1 do roadmap) — não
/// é semente de banco e nunca é gravado.
/// </summary>
internal static class MuralDemonstracao
{
	public static IReadOnlyList<PublicacaoViewModel> Publicacoes(DateTimeOffset agora) =>
	[
		new("Recesso de fim de ano: como registrar as férias",
			"O período de recesso vai de 23 de dezembro a 2 de janeiro. Quem for emendar férias precisa registrar a solicitação até o dia 30 deste mês.",
			agora.AddDays(-1), TipoPublicacao.Aviso, "Recursos Humanos", "recursos-humanos"),

		new("Manutenção programada da rede no sábado",
			"A rede interna ficará indisponível das 8h às 12h do próximo sábado para troca do link principal. Sistemas acessíveis pela internet seguem no ar.",
			agora.AddDays(-2), TipoPublicacao.Aviso, "Infraestrutura", "infraestrutura"),

		new("Encontro trimestral de resultados",
			"Apresentação dos números do trimestre e das prioridades do próximo, no auditório do 4º andar, com transmissão para quem estiver remoto.",
			agora.AddDays(-4), TipoPublicacao.Evento, "Diretoria", "diretoria"),

		new("Nova política de reembolso entra em vigor",
			"Despesas passam a ser lançadas pelo próprio portal, com prazo de cinco dias úteis para aprovação do gestor direto.",
			agora.AddDays(-6), TipoPublicacao.Noticia, "Financeiro", "financeiro"),

		new("Treinamento de segurança da informação",
			"Turmas abertas para as duas primeiras semanas do mês. A trilha é obrigatória para quem acessa dados de clientes.",
			agora.AddDays(-9), TipoPublicacao.Evento, "Infraestrutura", "infraestrutura"),

		new("Resultado da pesquisa de clima",
			"A participação chegou a 87%. Os planos de ação por área serão apresentados pelos gestores nas reuniões da próxima quinzena.",
			agora.AddDays(-14), TipoPublicacao.Noticia, "Recursos Humanos", "recursos-humanos"),
	];
}
