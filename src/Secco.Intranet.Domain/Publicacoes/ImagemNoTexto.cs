using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Publicacoes;

/// <summary>
/// Imagem inserida no meio do texto de uma <see cref="Publicacao"/>. Não guarda descrição:
/// ela fica no alt do Markdown, a única fonte.
/// </summary>
public sealed class ImagemNoTexto : BaseEntity
{
	private ImagemNoTexto()
	{
		// Construtor de rehidratação do EF Core
		CaminhoRelativo = string.Empty;
		ChaveEmbrulhada = string.Empty;
	}

	/// <summary>Cria a imagem de uma publicação.</summary>
	/// <param name="publicacaoId">Publicação dona. Obrigatória.</param>
	/// <param name="caminhoRelativo">Caminho do arquivo cifrado no armazenamento. Obrigatório.</param>
	/// <param name="chaveEmbrulhada">Chave de dados embrulhada pela chave mestra. Obrigatória.</param>
	/// <exception cref="DomainInvariantException">Se algum argumento faltar.</exception>
	public ImagemNoTexto(Guid publicacaoId, string caminhoRelativo, string chaveEmbrulhada)
	{
		if (publicacaoId == Guid.Empty)
		{
			throw new DomainInvariantException("A imagem exige uma publicação dona.");
		}

		if (string.IsNullOrWhiteSpace(caminhoRelativo))
		{
			throw new DomainInvariantException("A imagem exige o caminho do arquivo.");
		}

		if (string.IsNullOrWhiteSpace(chaveEmbrulhada))
		{
			throw new DomainInvariantException("A imagem exige a chave embrulhada.");
		}

		PublicacaoId = publicacaoId;
		CaminhoRelativo = caminhoRelativo;
		ChaveEmbrulhada = chaveEmbrulhada;
	}

	/// <summary>Publicação dona (coluna <c>id_fk_publicacao</c>).</summary>
	public Guid PublicacaoId { get; private set; }

	/// <summary>Caminho do arquivo cifrado (coluna <c>ds_caminho_relativo</c>).</summary>
	public string CaminhoRelativo { get; private set; }

	/// <summary>Chave de dados embrulhada (coluna <c>ds_chave_embrulhada</c>).</summary>
	public string ChaveEmbrulhada { get; private set; }
}
