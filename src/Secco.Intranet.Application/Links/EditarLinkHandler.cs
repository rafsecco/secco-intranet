using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>
/// Edita um link (nome, endereço, nova aba e grupo) ou um grupo (só o nome). O item e o grupo de
/// destino saem do <see cref="TrechoDeLinks"/>; nada é escrito antes de os dois estarem confirmados.
/// </summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class EditarLinkHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Novos dados; num grupo, <c>Rota</c> e <c>GrupoId</c> são ignorados.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result> HandleAsync(EditarLinkCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var arvore = await repository.ListarPorSetorAsync(command.SetorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);
		var item = trecho?.Item(command.Id);

		if (trecho is null || item is null)
		{
			return Result.Failure(IntranetErrors.Links.NotFound);
		}

		if (CamposDeLink.ValidarNome(command.Nome) is { } erroDeNome)
		{
			return Result.Failure(erroDeNome);
		}

		var nome = command.Nome!.Trim();
		var ehGrupo = item.Rota is null;
		string? endereco = null;
		var destinoId = item.ParentId!.Value;

		if (!ehGrupo)
		{
			if (CamposDeLink.ValidarEndereco(command.Rota, out var enderecoAparado) is { } erroDeEndereco)
			{
				return Result.Failure(erroDeEndereco);
			}

			endereco = enderecoAparado;

			// Sem GrupoId o link vai para o nó; GrupoId igual ao pai atual é "ficar onde está", mesmo
			// quando o pai é um grupo mais fundo, que o seletor de grupos não oferece.
			if (command.GrupoId != item.ParentId)
			{
				var destino = command.GrupoId is { } grupoId ? trecho.Grupo(grupoId) : trecho.No;

				if (destino is null)
				{
					return Result.Failure(IntranetErrors.Links.GrupoInvalido);
				}

				destinoId = destino.Id;
			}
		}

		var mudouDeGrupo = destinoId != item.ParentId;
		var irmaosDoDestino = trecho.FilhosDe(destinoId).Where(irmao => irmao.Id != item.Id).ToList();

		// Nome igual mantém o slug; se ele colide no novo pai (troca de grupo), gera outro.
		var slug = string.Equals(nome, item.Nome, StringComparison.Ordinal)
			&& !irmaosDoDestino.Any(irmao => string.Equals(irmao.Slug, item.Slug, StringComparison.Ordinal))
			? item.Slug
			: SlugDeLink.Gerar(nome, irmaosDoDestino.Select(irmao => irmao.Slug));

		// O item vem de ListarPorSetorAsync, sem rastreamento: a escrita é sobre a instância rastreada.
		var rastreado = await repository.GetParaEdicaoAsync(item.Id, cancellationToken).ConfigureAwait(false);

		if (rastreado is null)
		{
			return Result.Failure(IntranetErrors.Links.NotFound);
		}

		var paiAntigoId = item.ParentId!.Value;
		var irmaosDoPaiAntigo = trecho.FilhosDe(paiAntigoId).Where(irmao => irmao.Id != item.Id).ToList();

		rastreado.Editar(nome, slug, endereco, command.NovaAba);

		if (mudouDeGrupo)
		{
			rastreado.Reposicionar(destinoId, irmaosDoDestino.Count);

			await RenumerarAsync(irmaosDoPaiAntigo, cancellationToken).ConfigureAwait(false);
			await RenumerarAsync(irmaosDoDestino, cancellationToken).ConfigureAwait(false);
		}

		await repository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemEditar, rastreado,
			new { setorId = command.SetorId, nome = rastreado.Nome, slug = rastreado.Slug, rota = rastreado.Rota, novaAba = rastreado.NovaAba, parentId = rastreado.ParentId },
			cancellationToken).ConfigureAwait(false);

		return Result.Success();
	}

	// Renumera 0..n-1 pela ordem atual, fechando buracos; cada irmão é recarregado como rastreado.
	private async Task RenumerarAsync(List<Domain.Menu.ItemMenu> irmaos, CancellationToken cancellationToken)
	{
		for (var posicao = 0; posicao < irmaos.Count; posicao++)
		{
			var irmao = await repository.GetParaEdicaoAsync(irmaos[posicao].Id, cancellationToken).ConfigureAwait(false);
			irmao!.DefinirOrdem(posicao);
		}
	}
}
