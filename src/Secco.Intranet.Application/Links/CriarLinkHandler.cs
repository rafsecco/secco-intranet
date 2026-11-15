using Secco.Intranet.Application.Auditoria;
using Secco.Intranet.Application.Menu;
using Secco.Intranet.Domain.Menu;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Application.Links;

/// <summary>Cria um link por baixo do nó de links ou de um grupo dele, por último.</summary>
/// <param name="repository">Persistência da árvore.</param>
/// <param name="trilha">Trilha de auditoria.</param>
public sealed class CriarLinkHandler(IItemMenuRepository repository, ITrilhaDeAuditoria trilha)
{
	/// <summary>Executa o caso de uso.</summary>
	/// <param name="command">Dados do novo link.</param>
	/// <param name="cancellationToken">Token de cancelamento.</param>
	public async Task<Result<Guid>> HandleAsync(CriarLinkCommand command, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(command);

		var arvore = await repository.ListarPorSetorAsync(command.SetorId, cancellationToken).ConfigureAwait(false);
		var trecho = TrechoDeLinks.De(arvore);

		if (trecho is null)
		{
			return Result.Failure<Guid>(IntranetErrors.Links.NotFound);
		}

		if (CamposDeLink.ValidarNome(command.Nome) is { } erroDeNome)
		{
			return Result.Failure<Guid>(erroDeNome);
		}

		if (CamposDeLink.ValidarEndereco(command.Rota, out var endereco) is { } erroDeEndereco)
		{
			return Result.Failure<Guid>(erroDeEndereco);
		}

		var destino = command.GrupoId is { } grupoId ? trecho.Grupo(grupoId) : trecho.No;

		if (destino is null)
		{
			return Result.Failure<Guid>(IntranetErrors.Links.GrupoInvalido);
		}

		var nome = command.Nome!.Trim();
		var irmaos = trecho.FilhosDe(destino.Id);
		var slug = SlugDeLink.Gerar(nome, irmaos.Select(irmao => irmao.Slug));
		var ordem = irmaos.Select(irmao => irmao.Ordem).DefaultIfEmpty(-1).Max() + 1;

		var link = new ItemMenu(
			command.SetorId, destino.Id, nome, slug, TipoDeItemMenu.Personalizado, endereco, null, ordem,
			NivelDeAcessoItemMenu.Leitura, command.NovaAba);

		await repository.AddAsync(link, cancellationToken).ConfigureAwait(false);

		await AuditoriaDeMenu.ItemAsync(
			trilha, VerbosDeAuditoria.MenuItemCriar, link,
			new { setorId = link.SetorId, parentId = link.ParentId, nome = link.Nome, slug = link.Slug, tipo = link.Tipo.ToString(), rota = link.Rota, novaAba = link.NovaAba },
			cancellationToken).ConfigureAwait(false);

		return link.Id;
	}
}
