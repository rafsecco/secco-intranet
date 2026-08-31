using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Documentos;

/// <summary>Quem enxerga um documento.</summary>
public enum VisibilidadeDocumento
{
	/// <summary>Apenas quem tem a Role do setor dono (<c>{slug}-admin</c> ou <c>{slug}-user</c>).</summary>
	Setor = 0,

	/// <summary>Qualquer pessoa autenticada da instituição.</summary>
	Empresa = 1,
}

/// <summary>
/// Documento publicado por um setor. A entidade guarda apenas os metadados; os bytes ficam
/// no <c>IArquivoStore</c>, cifrados, endereçados por <see cref="CaminhoRelativo"/> e
/// destrancados pela <see cref="ChaveEmbrulhada"/>.
/// </summary>
public sealed class Documento : BaseEntity
{
	private Documento()
	{
		// Construtor de rehidratação do EF Core
		Titulo = string.Empty;
		NomeArquivo = string.Empty;
		ContentType = string.Empty;
		CaminhoRelativo = string.Empty;
		ChaveEmbrulhada = string.Empty;
		CriadoPor = string.Empty;
	}

	/// <summary>Cria um documento já gravado no armazenamento.</summary>
	/// <param name="setorId">Setor dono. Obrigatório.</param>
	/// <param name="titulo">Título de exibição. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="nomeArquivo">Nome original do arquivo enviado. Obrigatório.</param>
	/// <param name="contentType">Tipo apurado do conteúdo — nunca o informado pelo cliente.</param>
	/// <param name="tamanho">Tamanho em bytes do conteúdo original.</param>
	/// <param name="visibilidade">Quem enxerga o documento.</param>
	/// <param name="caminhoRelativo">Endereço opaco devolvido pelo armazenamento.</param>
	/// <param name="chaveEmbrulhada">Chave do arquivo, cifrada pela chave mestra.</param>
	/// <param name="criadoPor">Identificação de quem publicou.</param>
	/// <exception cref="DomainInvariantException">Se algum campo obrigatório estiver vazio ou o tamanho não for positivo.</exception>
	public Documento(
		Guid setorId,
		string titulo,
		string? descricao,
		string nomeArquivo,
		string contentType,
		long tamanho,
		VisibilidadeDocumento visibilidade,
		string caminhoRelativo,
		string chaveEmbrulhada,
		string criadoPor)
	{
		if (setorId == Guid.Empty)
		{
			throw new DomainInvariantException("Um documento exige um setor dono.");
		}

		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Um documento exige título não vazio.");
		}

		if (string.IsNullOrWhiteSpace(nomeArquivo))
		{
			throw new DomainInvariantException("Um documento exige o nome do arquivo.");
		}

		if (string.IsNullOrWhiteSpace(caminhoRelativo) || string.IsNullOrWhiteSpace(chaveEmbrulhada))
		{
			throw new DomainInvariantException("Um documento exige o endereço e a chave do conteúdo gravado.");
		}

		if (tamanho <= 0)
		{
			throw new DomainInvariantException("Um documento exige tamanho positivo.");
		}

		SetorId = setorId;
		Titulo = titulo.Trim();
		Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
		NomeArquivo = nomeArquivo;
		ContentType = contentType;
		Tamanho = tamanho;
		Visibilidade = visibilidade;
		CaminhoRelativo = caminhoRelativo;
		ChaveEmbrulhada = chaveEmbrulhada;
		CriadoPor = criadoPor;
		Ativo = true;
		CreatedAt = DateTimeOffset.UtcNow;
	}

	/// <summary>Setor dono (coluna <c>id_fk_setor</c>).</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Título de exibição (coluna <c>ds_titulo</c>).</summary>
	public string Titulo { get; private set; }

	/// <summary>Descrição livre (coluna <c>ds_descricao</c>).</summary>
	public string? Descricao { get; private set; }

	/// <summary>Nome original do arquivo enviado (coluna <c>ds_nome_arquivo</c>).</summary>
	public string NomeArquivo { get; private set; }

	/// <summary>Tipo apurado do conteúdo (coluna <c>ds_content_type</c>).</summary>
	public string ContentType { get; private set; }

	/// <summary>Tamanho do conteúdo original, em bytes (coluna <c>nr_tamanho</c>).</summary>
	public long Tamanho { get; private set; }

	/// <summary>Quem enxerga o documento (coluna <c>ie_visibilidade</c>).</summary>
	public VisibilidadeDocumento Visibilidade { get; private set; }

	/// <summary>Endereço opaco no armazenamento (coluna <c>ds_caminho_relativo</c>).</summary>
	public string CaminhoRelativo { get; private set; }

	/// <summary>
	/// Chave do arquivo cifrada pela chave mestra, no formato <c>secco-enc:v1:</c>
	/// (coluna <c>ds_chave_embrulhada</c>). Rotacionar a chave mestra reescreve só esta
	/// coluna — nunca o arquivo.
	/// </summary>
	public string ChaveEmbrulhada { get; private set; }

	/// <summary>Identificação de quem publicou (coluna <c>ds_criado_por</c>).</summary>
	public string CriadoPor { get; private set; }

	/// <summary>Documento disponível para leitura (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da publicação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Troca quem enxerga o documento.</summary>
	/// <param name="visibilidade">Nova visibilidade.</param>
	public void AlterarVisibilidade(VisibilidadeDocumento visibilidade) => Visibilidade = visibilidade;

	/// <summary>Retira o documento de circulação, preservando o registro e o arquivo.</summary>
	public void Arquivar() => Ativo = false;
}
