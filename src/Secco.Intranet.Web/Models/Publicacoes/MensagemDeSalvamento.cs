using Secco.Intranet.Application.Publicacoes.Notificacao;

namespace Secco.Intranet.Web.Models.Publicacoes;

/// <summary>
/// Traduz o relatório de notificação para a frase que quem publicou lê. Contar a verdade na
/// mesma tela é o que dispensa qualquer consulta posterior de status.
/// </summary>
public static class MensagemDeSalvamento
{
	/// <summary>Monta a mensagem.</summary>
	/// <param name="titulo">Título da publicação salva.</param>
	/// <param name="relatorio">Relatório do aviso; nulo quando a operação foi uma edição.</param>
	public static string Montar(string titulo, RelatorioDeNotificacao? relatorio)
	{
		var mensagem = $"Publicação \"{titulo}\" salva.";

		if (relatorio is null)
		{
			return mensagem;
		}

		if (relatorio.Indisponivel)
		{
			return mensagem + " O aviso não pôde ser enviado agora — a publicação está no ar mesmo assim.";
		}

		if (relatorio.ProgramadaPara is not null)
		{
			return mensagem
				+ $" O aviso será enviado em {relatorio.ProgramadaPara.Value.ToLocalTime():dd/MM/yyyy 'às' HH:mm}, quando a publicação entrar no ar.";
		}

		if (relatorio.Notificados > 0)
		{
			mensagem += $" {relatorio.Notificados} {(relatorio.Notificados == 1 ? "pessoa avisada" : "pessoas avisadas")}.";
		}

		if (relatorio.SemEmail > 0)
		{
			mensagem += $" {relatorio.SemEmail} sem e-mail cadastrado não {(relatorio.SemEmail == 1 ? "recebeu" : "receberam")} por e-mail.";
		}

		return mensagem;
	}
}
