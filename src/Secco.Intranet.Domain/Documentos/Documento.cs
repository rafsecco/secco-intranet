using Secco.SharedKernel.Entities;
using Secco.SharedKernel.Exceptions;

namespace Secco.Intranet.Domain.Documentos;

/// <summary>Natureza de um item da lista de documentos.</summary>
public enum TipoDeDocumento
{
	/// <summary>Documento com arquivo anexado.</summary>
	Arquivo = 1,

	/// <summary>Grupo sem arquivo, que contém documentos abaixo dele.</summary>
	Grupo = 2,
}

/// <summary>
/// Documento publicado por um setor, ou grupo que reúne documentos. Para um arquivo, a entidade
/// guarda apenas os metadados; os bytes ficam no <c>IArquivoStore</c>, cifrados, endereçados por
/// <see cref="CaminhoRelativo"/> e destrancados pela <see cref="ChaveEmbrulhada"/>. Um grupo não
/// tem arquivo, então esses campos são nulos.
/// </summary>
public sealed class Documento : BaseEntity
{
	private Documento()
	{
		// Construtor de rehidratação do EF Core
		Titulo = string.Empty;
		CriadoPor = string.Empty;
	}

	/// <summary>Cria um documento (arquivo) já gravado no armazenamento.</summary>
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
	/// <param name="parentId">Grupo que contém o documento; <c>null</c> na raiz.</param>
	/// <param name="ordem">Posição entre os irmãos, a partir de zero.</param>
	/// <exception cref="DomainInvariantException">Se algum campo obrigatório estiver vazio ou o tamanho não for positivo.</exception>
	public Documento(
		Guid setorId,
		string titulo,
		string? descricao,
		string nomeArquivo,
		string contentType,
		long tamanho,
		Visibilidade visibilidade,
		string caminhoRelativo,
		string chaveEmbrulhada,
		string criadoPor,
		Guid? parentId = null,
		int ordem = 0)
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

		Tipo = TipoDeDocumento.Arquivo;
		ParentId = parentId;
		Ordem = ordem;
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

	/// <summary>
	/// Cria um grupo: item sem arquivo que apenas agrupa documentos abaixo dele. A visibilidade é
	/// sempre <see cref="Visibilidade.Setor"/>, pois não há conteúdo a proteger.
	/// </summary>
	/// <param name="setorId">Setor dono. Obrigatório.</param>
	/// <param name="titulo">Título de exibição. Obrigatório.</param>
	/// <param name="descricao">Descrição livre. Opcional.</param>
	/// <param name="criadoPor">Identificação de quem criou.</param>
	/// <returns>O grupo, ativo, na raiz e na posição zero.</returns>
	/// <exception cref="DomainInvariantException">Se o setor ou o título estiverem vazios.</exception>
	public static Documento CriarGrupo(Guid setorId, string titulo, string? descricao, string criadoPor)
	{
		if (setorId == Guid.Empty)
		{
			throw new DomainInvariantException("Um grupo exige um setor dono.");
		}

		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Um grupo exige título não vazio.");
		}

		return new Documento
		{
			Tipo = TipoDeDocumento.Grupo,
			SetorId = setorId,
			Titulo = titulo.Trim(),
			Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim(),
			Visibilidade = Visibilidade.Setor,
			CriadoPor = criadoPor,
			Ativo = true,
			CreatedAt = DateTimeOffset.UtcNow,
		};
	}

	/// <summary>Arquivo ou grupo (coluna <c>ie_tipo</c>).</summary>
	public TipoDeDocumento Tipo { get; private set; }

	/// <summary>Grupo que contém este item; <c>null</c> na raiz (coluna <c>id_fk_parent</c>).</summary>
	public Guid? ParentId { get; private set; }

	/// <summary>Posição entre os irmãos ativos, a partir de zero (coluna <c>nr_ordem</c>).</summary>
	public int Ordem { get; private set; }

	/// <summary>Indica se o item é um grupo, sem arquivo.</summary>
	public bool EhGrupo => Tipo == TipoDeDocumento.Grupo;

	/// <summary>Setor dono (coluna <c>id_fk_setor</c>).</summary>
	public Guid SetorId { get; private set; }

	/// <summary>Título de exibição (coluna <c>ds_titulo</c>).</summary>
	public string Titulo { get; private set; }

	/// <summary>Descrição livre (coluna <c>ds_descricao</c>).</summary>
	public string? Descricao { get; private set; }

	/// <summary>Nome original do arquivo enviado; nulo no grupo (coluna <c>ds_nome_arquivo</c>).</summary>
	public string? NomeArquivo { get; private set; }

	/// <summary>Tipo apurado do conteúdo; nulo no grupo (coluna <c>ds_content_type</c>).</summary>
	public string? ContentType { get; private set; }

	/// <summary>Tamanho do conteúdo original, em bytes; nulo no grupo (coluna <c>nr_tamanho</c>).</summary>
	public long? Tamanho { get; private set; }

	/// <summary>Quem enxerga o documento (coluna <c>ie_visibilidade</c>).</summary>
	public Visibilidade Visibilidade { get; private set; }

	/// <summary>Endereço opaco no armazenamento; nulo no grupo (coluna <c>ds_caminho_relativo</c>).</summary>
	public string? CaminhoRelativo { get; private set; }

	/// <summary>
	/// Chave do arquivo cifrada pela chave mestra, no formato <c>secco-enc:v1:</c>
	/// (coluna <c>ds_chave_embrulhada</c>); nula no grupo. Rotacionar a chave mestra reescreve só
	/// esta coluna — nunca o arquivo.
	/// </summary>
	public string? ChaveEmbrulhada { get; private set; }

	/// <summary>Identificação de quem publicou (coluna <c>ds_criado_por</c>).</summary>
	public string CriadoPor { get; private set; }

	/// <summary>Documento disponível para leitura (coluna <c>fl_ativo</c>).</summary>
	public bool Ativo { get; private set; }

	/// <summary>Momento da publicação (coluna <c>dt_created_at</c>).</summary>
	public DateTimeOffset CreatedAt { get; private set; }

	/// <summary>Troca quem enxerga o documento.</summary>
	/// <param name="visibilidade">Nova visibilidade.</param>
	/// <exception cref="DomainInvariantException">Se for um grupo e a visibilidade não for <see cref="Visibilidade.Setor"/>.</exception>
	public void AlterarVisibilidade(Visibilidade visibilidade)
	{
		if (EhGrupo && visibilidade != Visibilidade.Setor)
		{
			throw new DomainInvariantException("Um grupo só pode ter visibilidade de setor.");
		}

		Visibilidade = visibilidade;
	}

	/// <summary>Edita os metadados de exibição, valendo para arquivo e grupo.</summary>
	/// <param name="titulo">Novo título. Obrigatório.</param>
	/// <param name="descricao">Nova descrição; vazia vira <c>null</c>.</param>
	/// <exception cref="DomainInvariantException">Se o título estiver vazio.</exception>
	public void Editar(string titulo, string? descricao)
	{
		if (string.IsNullOrWhiteSpace(titulo))
		{
			throw new DomainInvariantException("Um documento exige título não vazio.");
		}

		Titulo = titulo.Trim();
		Descricao = string.IsNullOrWhiteSpace(descricao) ? null : descricao.Trim();
	}

	/// <summary>
	/// Troca o arquivo do documento: nome, tipo, tamanho, endereço e chave mudam juntos.
	/// </summary>
	/// <param name="nomeArquivo">Nome original do novo arquivo. Obrigatório.</param>
	/// <param name="contentType">Tipo apurado do novo conteúdo.</param>
	/// <param name="tamanho">Tamanho em bytes do novo conteúdo.</param>
	/// <param name="caminhoRelativo">Endereço do novo conteúdo no armazenamento.</param>
	/// <param name="chaveEmbrulhada">Chave do novo arquivo, cifrada pela chave mestra.</param>
	/// <returns>O <see cref="CaminhoRelativo"/> anterior, para o chamador remover o arquivo antigo.</returns>
	/// <exception cref="DomainInvariantException">Se for um grupo, ou se algum campo obrigatório estiver vazio ou o tamanho não for positivo.</exception>
	public string TrocarArquivo(string nomeArquivo, string contentType, long tamanho, string caminhoRelativo, string chaveEmbrulhada)
	{
		if (EhGrupo)
		{
			throw new DomainInvariantException("Um grupo não tem arquivo para trocar.");
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

		var anterior = CaminhoRelativo!;
		NomeArquivo = nomeArquivo;
		ContentType = contentType;
		Tamanho = tamanho;
		CaminhoRelativo = caminhoRelativo;
		ChaveEmbrulhada = chaveEmbrulhada;
		return anterior;
	}

	/// <summary>Coloca o item em um pai e uma posição.</summary>
	/// <param name="parentId">Novo pai; <c>null</c> para a raiz.</param>
	/// <param name="ordem">Nova posição entre os irmãos.</param>
	public void Posicionar(Guid? parentId, int ordem)
	{
		ParentId = parentId;
		Ordem = ordem;
	}

	/// <summary>Retira o documento de circulação, preservando o registro e o arquivo.</summary>
	public void Arquivar() => Ativo = false;
}
