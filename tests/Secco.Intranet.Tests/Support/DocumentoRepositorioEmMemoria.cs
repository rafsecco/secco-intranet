using Secco.Intranet.Application.Documentos;
using Secco.Intranet.Domain.Bibliotecas;
using Secco.Intranet.Domain.Documentos;
using Secco.Intranet.Domain.Setores;

namespace Secco.Intranet.Tests.Support;

/// <summary>
/// <see cref="IDocumentoRepository"/> sobre uma lista, com a mesma ordem (<c>Ordem</c>, mais recente
/// primeiro, <c>Id</c>) e o mesmo filtro de ativos do repositório real.
/// </summary>
public sealed class DocumentoRepositorioEmMemoria : IDocumentoRepository
{
	private readonly Dictionary<Guid, Setor> _setores = [];
	private readonly Dictionary<Guid, Biblioteca> _bibliotecas = [];

	/// <summary>Documentos existentes — os testes montam o cenário mexendo aqui direto.</summary>
	public List<Documento> Documentos { get; } = [];

	/// <summary>Quantas vezes as alterações foram gravadas (<c>AddAsync</c> e <c>SaveChangesAsync</c>).</summary>
	public int Gravacoes { get; private set; }

	/// <summary>Registra o setor que os documentos referenciam.</summary>
	/// <param name="setor">Setor dono.</param>
	public void RegistrarSetor(Setor setor) => _setores[setor.Id] = setor;

	/// <summary>Registra a biblioteca que os documentos referenciam.</summary>
	/// <param name="biblioteca">Biblioteca dona.</param>
	public void RegistrarBiblioteca(Biblioteca biblioteca) => _bibliotecas[biblioteca.Id] = biblioteca;

	public Task AddAsync(Documento documento, CancellationToken cancellationToken = default)
	{
		Documentos.Add(documento);
		Gravacoes++;

		return Task.CompletedTask;
	}

	public Task SaveChangesAsync(CancellationToken cancellationToken = default)
	{
		Gravacoes++;

		return Task.CompletedTask;
	}

	public Task<DocumentoComDono?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
	{
		var documento = Documentos.FirstOrDefault(item => item.Id == id);

		if (documento is null)
		{
			return Task.FromResult<DocumentoComDono?>(null);
		}

		var (referencia, nome, ativo) = Dono(documento);

		return Task.FromResult<DocumentoComDono?>(new DocumentoComDono(documento, referencia, nome, ativo));
	}

	public Task<IReadOnlyList<DocumentoDto>> ListarPorDonoAsync(
		ReferenciaDeDono dono,
		CancellationToken cancellationToken = default)
	{
		var lista = Ordenar(Documentos.Where(item => item.Ativo && Dono(item).Referencia == dono))
			.Select(item =>
			{
				var (referencia, nome, _) = Dono(item);

				return new DocumentoDto(
					item.Id,
					item.Titulo,
					item.Descricao,
					item.NomeArquivo,
					item.ContentType,
					item.Tamanho,
					item.Visibilidade,
					referencia,
					nome,
					item.CriadoPor,
					item.CreatedAt,
					item.Tipo,
					item.ParentId,
					item.Ordem);
			})
			.ToList();

		return Task.FromResult<IReadOnlyList<DocumentoDto>>(lista);
	}

	public Task<IReadOnlyList<Documento>> ListarIrmaosAsync(
		DonoDeDocumento dono,
		Guid? parentId,
		CancellationToken cancellationToken = default) =>
		Task.FromResult<IReadOnlyList<Documento>>(
			[.. Ordenar(Documentos.Where(item => item.Dono == dono && item.ParentId == parentId && item.Ativo))]);

	public Task<bool> TemFilhosAtivosAsync(Guid documentoId, CancellationToken cancellationToken = default) =>
		Task.FromResult(Documentos.Any(item => item.ParentId == documentoId && item.Ativo));

	private (ReferenciaDeDono Referencia, string Nome, bool Ativo) Dono(Documento documento)
	{
		if (documento.BibliotecaId is { } bibliotecaId)
		{
			var biblioteca = _bibliotecas[bibliotecaId];

			return (ReferenciaDeDono.DaBiblioteca(biblioteca.Slug), biblioteca.Nome, biblioteca.Ativo);
		}

		var setor = _setores[documento.SetorId!.Value];

		return (ReferenciaDeDono.DoSetor(setor.Slug), setor.Nome, setor.Ativo);
	}

	private static IOrderedEnumerable<Documento> Ordenar(IEnumerable<Documento> origem) =>
		origem.OrderBy(item => item.Ordem).ThenByDescending(item => item.CreatedAt).ThenBy(item => item.Id);
}
