using Microsoft.Extensions.Logging;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Application.Documentos;
using Secco.SharedKernel.Results;

namespace Secco.Intranet.Tests.Support;

/// <summary>Armazenamento de arquivos em memória que registra gravações e remoções.</summary>
public sealed class ArquivoStoreFalso : IArquivoStore
{
	/// <summary>Gravações na ordem: pasta e caminho gerado.</summary>
	public List<(string Pasta, string Caminho)> Gravados { get; } = [];

	/// <summary>Caminhos removidos, na ordem.</summary>
	public List<string> Removidos { get; } = [];

	/// <summary>Quando informado, lido a cada remoção para saber quantos salvamentos já tinham ocorrido.</summary>
	public Func<int>? ContadorDeSalvamentos { get; set; }

	/// <summary>Salvamentos já feitos no instante de cada remoção.</summary>
	public List<int> SalvamentosNasRemocoes { get; } = [];

	/// <summary>Quando verdadeiro, <see cref="RemoverAsync"/> lança.</summary>
	public bool FalharAoRemover { get; set; }

	/// <inheritdoc />
	public Task<ArquivoGravado> GravarAsync(Stream conteudo, CancellationToken cancellationToken = default) =>
		GravarAsync(conteudo, "documentos", cancellationToken);

	/// <inheritdoc />
	public Task<ArquivoGravado> GravarAsync(Stream conteudo, string pastaLogica, CancellationToken cancellationToken = default)
	{
		var caminho = $"{pastaLogica}/{Guid.NewGuid():N}.bin";

		Gravados.Add((pastaLogica, caminho));

		return Task.FromResult(new ArquivoGravado(caminho, "chave-" + caminho, 3));
	}

	/// <inheritdoc />
	public Task EscreverEmAsync(string caminhoRelativo, string chaveEmbrulhada, Stream destino, CancellationToken cancellationToken = default) =>
		throw new NotSupportedException();

	/// <inheritdoc />
	public Task RemoverAsync(string caminhoRelativo, CancellationToken cancellationToken = default)
	{
		SalvamentosNasRemocoes.Add(ContadorDeSalvamentos?.Invoke() ?? 0);

		if (FalharAoRemover)
		{
			throw new IOException($"Arquivo {caminhoRelativo} inacessível.");
		}

		Removidos.Add(caminhoRelativo);

		return Task.CompletedTask;
	}
}

/// <summary>Processador de imagem que devolve bytes fixos ou um erro.</summary>
public sealed class ProcessadorDeImagemFalso : IProcessadorDeImagem
{
	/// <summary>Quando não nulo, o processamento falha com este erro.</summary>
	public Error? FalharCom { get; set; }

	/// <summary>Quantas vezes foi chamado.</summary>
	public int Chamadas { get; private set; }

	/// <inheritdoc />
	public Task<Result<FotoProcessada>> ProcessarFotoAsync(Stream upload, RecorteDaFoto? recorte, CancellationToken cancellationToken = default)
	{
		Chamadas++;

		return Task.FromResult(FalharCom is null
			? Result.Success(new FotoProcessada([1, 2, 3], "image/webp"))
			: Result.Failure<FotoProcessada>(FalharCom));
	}
}

/// <summary>Logger que guarda as mensagens.</summary>
/// <typeparam name="T">Categoria.</typeparam>
public sealed class LoggerCapturador<T> : ILogger<T>
{
	/// <summary>Nível, texto e exceção de cada entrada.</summary>
	public List<(LogLevel Nivel, string Mensagem, Exception? Excecao)> Entradas { get; } = [];

	/// <inheritdoc />
	public IDisposable? BeginScope<TState>(TState state)
		where TState : notnull => null;

	/// <inheritdoc />
	public bool IsEnabled(LogLevel logLevel) => true;

	/// <inheritdoc />
	public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
		Entradas.Add((logLevel, formatter(state, exception), exception));
}
