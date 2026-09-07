using Secco.Intranet.Domain;

namespace Secco.Intranet.Application.Publicacoes.Notificacao;

/// <summary>
/// Transforma uma publicação recém-criada nos lotes que o Hub recebe. Fica separado do
/// handler porque é a parte que mais tem regra e menos tem dependência: nada aqui toca banco,
/// HTTP ou relógio.
/// </summary>
internal static class AvisoDePublicacao
{
	/// <summary>Teto de destinos por lote, imposto pelo <c>MaxBatchDestinations</c> do Hub.</summary>
	internal const int MaxDestinosPorLote = 500;

	/// <summary>Valor de <c>Source</c> em toda notificação criada pelo Mural.</summary>
	internal const string Origem = "mural";

	/// <summary>Quem deve receber o aviso desta publicação.</summary>
	/// <param name="usuarios">Usuários do tenant.</param>
	/// <param name="visibilidade">Visibilidade da publicação.</param>
	/// <param name="setorSlug">Slug do setor dono.</param>
	/// <param name="autorId">Quem publicou; sai da lista.</param>
	internal static IReadOnlyList<UsuarioDoTenant> Destinatarios(
		IReadOnlyList<UsuarioDoTenant> usuarios,
		Visibilidade visibilidade,
		string setorSlug,
		Guid? autorId)
	{
		var roles = new[] { $"{setorSlug}-admin", $"{setorSlug}-user" };

		return usuarios
			.Where(usuario => autorId is null || usuario.Id != autorId)
			.Where(usuario => visibilidade == Visibilidade.Empresa
				|| usuario.Roles.Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase)))
			.ToList();
	}

	/// <summary>
	/// Separa quem o lote pode levar de quem ficaria de fora. O lote do Hub é tudo-ou-nada e
	/// reprova inteiro por um destino inválido, então a partição acontece aqui — e sai de
	/// graça, porque a lista já está sendo percorrida.
	/// </summary>
	/// <param name="destinatarios">Quem deve receber.</param>
	/// <param name="canais">Canais pedidos.</param>
	internal static (IReadOnlyList<DestinoDaMensagem> Validos, int SemEmail) Particionar(
		IReadOnlyList<UsuarioDoTenant> destinatarios,
		IReadOnlyList<string> canais)
	{
		var exigeEmail = canais.Contains(CanaisDaPrioridade.Email, StringComparer.Ordinal);
		var validos = new List<DestinoDaMensagem>();
		var semEmail = 0;

		foreach (var usuario in destinatarios)
		{
			var temEmail = !string.IsNullOrWhiteSpace(usuario.Email);

			if (exigeEmail && !temEmail)
			{
				semEmail++;

				continue;
			}

			validos.Add(new DestinoDaMensagem(usuario.Id, exigeEmail ? usuario.Email : null));
		}

		return (validos, semEmail);
	}

	/// <summary>Fatia os destinos em lotes do tamanho que o Hub aceita.</summary>
	/// <param name="destinos">Destinos válidos.</param>
	internal static IEnumerable<IReadOnlyList<DestinoDaMensagem>> Fatiar(
		IReadOnlyList<DestinoDaMensagem> destinos) =>
		destinos.Chunk(MaxDestinosPorLote).Select(bloco => (IReadOnlyList<DestinoDaMensagem>)bloco);
}
