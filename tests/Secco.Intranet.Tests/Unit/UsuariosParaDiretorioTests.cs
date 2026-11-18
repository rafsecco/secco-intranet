using AwesomeAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Acesso;
using Secco.Intranet.Application.Diretorio;
using Secco.Intranet.Infrastructure.Diretorio;
using Secco.Intranet.Tests.Support;
using Secco.SDK.AspNetCore.Tenancy;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

public class UsuariosParaDiretorioTests
{
	private static readonly Guid Tenant = Guid.NewGuid();
	private static readonly Guid Ana = Guid.NewGuid();
	private static readonly Guid Bruno = Guid.NewGuid();
	private static readonly Guid Carla = Guid.NewGuid();

	private sealed class TenantContextFalso(Guid? tenantId) : ITenantContext
	{
		public Guid? TenantId => tenantId;

		public bool IsResolved => tenantId is not null;
	}

	private static UsuariosParaDiretorioDoSecureGate Montar(IGestaoDeAcesso gestao, IMemoryCache? cache = null, Guid? tenant = null, ILeitorDoDiretorio? leitor = null, ILogger<UsuariosParaDiretorioDoSecureGate>? logger = null) =>
		new(gestao, cache ?? new MemoryCache(new MemoryCacheOptions()), new TenantContextFalso(tenant ?? Tenant), leitor, logger ?? NullLogger<UsuariosParaDiretorioDoSecureGate>.Instance);

	private sealed class LeitorFalso : ILeitorDoDiretorio
	{
		public int Leituras { get; private set; }

		public Func<IReadOnlyList<UsuarioParaDiretorio>>? Resposta { get; set; }

		public Task<IReadOnlyList<UsuarioParaDiretorio>> ListarAtivosAsync(CancellationToken cancellationToken)
		{
			Leituras++;

			return Task.FromResult(Resposta!());
		}
	}

	private static readonly AtributosDoEntra Atributos = new("Analista", "TI", "123", null);

	public UsuariosParaDiretorioTests() => UsuariosParaDiretorioDoSecureGate.RearmarAviso();

	[Fact]
	public async Task Listar_LeitorFunciona_UsaDiretorioEAtributos()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Bruno, "gestao@x.com");
		var leitor = new LeitorFalso { Resposta = () => [new UsuarioParaDiretorio(Ana, "ana@x.com", "Ana", Atributos)] };

		var resultado = await Montar(gestao, leitor: leitor).ListarAtivosAsync();

		resultado.Value.Should().ContainSingle().Which.Entra.Should().Be(Atributos);
		gestao.Listagens.Should().Be(0, "com o diretório respondendo, a gestão não é consultada");
	}

	[Fact]
	public async Task Listar_LeitorLanca_CaiParaGestaoSemAtributos()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var leitor = new LeitorFalso { Resposta = () => throw new HttpRequestException("fora") };
		var log = new Support.LoggerCapturador<UsuariosParaDiretorioDoSecureGate>();
		var adaptador = Montar(gestao, leitor: leitor, logger: log);

		var primeira = await adaptador.ListarAtivosAsync();
		adaptador.Esquecer();
		await adaptador.ListarAtivosAsync();

		primeira.Value.Single().Entra.Should().BeNull();
		gestao.Listagens.Should().Be(2);
		log.Entradas.Where(e => e.Nivel == LogLevel.Warning).Should().ContainSingle("o aviso sai uma vez por processo");
	}

	[Fact]
	public async Task Listar_FalhaNaSegundaPagina_CaiParaGestaoSemListaParcial()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var leitor = new LeitorFalso { Resposta = () => throw new Secco.SecureGate.Client.ApiException("500", 500, "", new Dictionary<string, IEnumerable<string>>(), null) };

		var resultado = await Montar(gestao, leitor: leitor).ListarAtivosAsync();

		resultado.Value.Select(u => u.Id).Should().Equal(Ana);
	}

	[Fact]
	public async Task Listar_LeitorEGestaoFalham_Indisponivel()
	{
		var gestao = new ContadorDeListagens { ErroDeListagem = IntranetErrors.Acesso.Indisponivel };
		var leitor = new LeitorFalso { Resposta = () => throw new HttpRequestException("fora") };

		var resultado = await Montar(gestao, leitor: leitor).ListarAtivosAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Listar_CacheValeParaAsDuasFontes()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var doLeitor = new LeitorFalso { Resposta = () => [new UsuarioParaDiretorio(Ana, "ana@x.com")] };
		var cacheA = new MemoryCache(new MemoryCacheOptions());
		var adaptadorA = Montar(gestao, cacheA, leitor: doLeitor);
		await adaptadorA.ListarAtivosAsync();
		await adaptadorA.ListarAtivosAsync();

		var caiu = new LeitorFalso { Resposta = () => throw new HttpRequestException("fora") };
		var adaptadorB = Montar(gestao, leitor: caiu);
		await adaptadorB.ListarAtivosAsync();
		await adaptadorB.ListarAtivosAsync();

		doLeitor.Leituras.Should().Be(1);
		caiu.Leituras.Should().Be(1);
		gestao.Listagens.Should().Be(1);
	}

	[Fact]
	public async Task ExcluiOsDesativados_EMantemBloqueados()
	{
		var gestao = new GestaoDeAcessoFalsa()
			.ComUsuario(Ana, "ana@x.com")
			.ComUsuario(Bruno, "bruno@x.com", SituacaoDoUsuario.Desativado)
			.ComUsuario(Carla, "carla@x.com", SituacaoDoUsuario.Bloqueado);

		var resultado = await Montar(gestao).ListarAtivosAsync();

		resultado.Value.Select(u => u.Id).Should().BeEquivalentTo([Ana, Carla],
			"bloqueio por tentativas é temporário; só desativado sai do diretório");
	}

	[Fact]
	public async Task GuardaEmCache_ESoConsultaAPlataformaUmaVez()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var adaptador = Montar(gestao);

		await adaptador.ListarAtivosAsync();
		await adaptador.ListarAtivosAsync();

		gestao.Listagens.Should().Be(1);
	}

	[Fact]
	public async Task NaoGuardaFalhaEmCache()
	{
		var gestao = new ContadorDeListagens { ErroDeListagem = IntranetErrors.Acesso.Indisponivel };
		var adaptador = Montar(gestao);

		var primeira = await adaptador.ListarAtivosAsync();
		gestao.ErroDeListagem = null;
		gestao.ComUsuario(Ana, "ana@x.com");
		var segunda = await adaptador.ListarAtivosAsync();

		primeira.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		segunda.IsSuccess.Should().BeTrue("uma falha não pode ficar 60 s escondendo um diretório que voltou");
		segunda.Value.Should().ContainSingle();
	}

	[Fact]
	public async Task CacheEPorTenant()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Ana, "ana@x.com");
		var cache = new MemoryCache(new MemoryCacheOptions());

		await Montar(gestao, cache, Guid.NewGuid()).ListarAtivosAsync();
		await Montar(gestao, cache, Guid.NewGuid()).ListarAtivosAsync();

		gestao.Listagens.Should().Be(2);
	}

	[Fact]
	public async Task TenantNaoResolvido_Falha_SemChamarAPlataforma()
	{
		var gestao = new ContadorDeListagens();
		var adaptador = new UsuariosParaDiretorioDoSecureGate(gestao, new MemoryCache(new MemoryCacheOptions()), new TenantContextFalso(null), null, NullLogger<UsuariosParaDiretorioDoSecureGate>.Instance);

		var resultado = await adaptador.ListarAtivosAsync();

		resultado.IsFailure.Should().BeTrue();
		gestao.Listagens.Should().Be(0);
	}

	[Fact]
	public async Task Indisponivel_ResponderNaoConfigurado()
	{
		var resultado = await new UsuariosParaDiretorioIndisponivel().ListarAtivosAsync();

		resultado.Error.Should().Be(IntranetErrors.Acesso.NaoConfigurado);
	}

	[Fact]
	public async Task DeDesenvolvimento_DevolveAsPessoasFicticias()
	{
		var resultado = await new UsuariosParaDiretorioDeDesenvolvimento().ListarAtivosAsync();

		resultado.Value.Should().HaveCount(PessoasDeDesenvolvimento.Todas.Count);
		resultado.Value.Select(u => u.Email).Should().OnlyContain(email => email.EndsWith("@exemplo.local"));
	}

	/// <summary>Conta quantas vezes a plataforma foi consultada e permite forçar falha na listagem.</summary>
	private sealed class ContadorDeListagens : IGestaoDeAcesso
	{
		private readonly GestaoDeAcessoFalsa _interno = new();

		public int Listagens { get; private set; }

		public Secco.SharedKernel.Results.Error? ErroDeListagem { get; set; }

		public ContadorDeListagens ComUsuario(Guid id, string email)
		{
			_interno.ComUsuario(id, email);

			return this;
		}

		public Task<Secco.SharedKernel.Results.Result<IReadOnlyList<UsuarioDto>>> ListarUsuariosAsync(CancellationToken cancellationToken = default)
		{
			Listagens++;

			return ErroDeListagem is null
				? _interno.ListarUsuariosAsync(cancellationToken)
				: Task.FromResult(Secco.SharedKernel.Results.Result.Failure<IReadOnlyList<UsuarioDto>>(ErroDeListagem));
		}

		public Task<Secco.SharedKernel.Results.Result<IReadOnlyList<PerfilDto>>> ListarPerfisAsync(CancellationToken cancellationToken = default) => _interno.ListarPerfisAsync(cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<PerfilDetalheDto>> ObterPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.ObterPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<PaginaDeMembros>> ListarMembrosAsync(string nome, int pagina, CancellationToken cancellationToken = default) => _interno.ListarMembrosAsync(nome, pagina, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result<UsuarioDetalheDto>> ObterUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.ObterUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> CriarPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.CriarPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> ExcluirPerfilAsync(string nome, CancellationToken cancellationToken = default) => _interno.ExcluirPerfilAsync(nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> AtribuirPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) => _interno.AtribuirPerfilAsync(usuarioId, perfil, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> RetirarPerfilAsync(Guid usuarioId, string perfil, CancellationToken cancellationToken = default) => _interno.RetirarPerfilAsync(usuarioId, perfil, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> DesativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.DesativarUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> ReativarUsuarioAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.ReativarUsuarioAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> EncerrarSessoesAsync(Guid usuarioId, CancellationToken cancellationToken = default) => _interno.EncerrarSessoesAsync(usuarioId, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> DefinirNomeDeExibicaoAsync(Guid usuarioId, string? nome, CancellationToken cancellationToken = default) => _interno.DefinirNomeDeExibicaoAsync(usuarioId, nome, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> GarantirPermissoesAsync(string nome, IReadOnlyCollection<string> permissoesMinimas, CancellationToken cancellationToken = default) => _interno.GarantirPermissoesAsync(nome, permissoesMinimas, cancellationToken);

		public Task<Secco.SharedKernel.Results.Result> DefinirPermissoesDoPerfilAsync(string nome, IReadOnlyCollection<string> permissoes, CancellationToken cancellationToken = default) => _interno.DefinirPermissoesDoPerfilAsync(nome, permissoes, cancellationToken);
	}

	[Fact]
	public async Task SecureGate_TrazONome()
	{
		var id = Guid.NewGuid();
		var gestao = new ContadorDeListagens().ComUsuario(id, "ana@x.com");
		// ComUsuario do contador delega ao GestaoDeAcessoFalsa interno; o nome entra pelo dublê.
		await gestao.DefinirNomeDeExibicaoAsync(id, "Ana Ribeiro");

		var lidos = await Montar(gestao).ListarAtivosAsync();

		lidos.Value.Single().Nome.Should().Be("Ana Ribeiro");
	}

	[Fact]
	public async Task Esquecer_FazAProximaListagemIrAPlataforma()
	{
		var gestao = new ContadorDeListagens().ComUsuario(Guid.NewGuid(), "ana@x.com");
		var adaptador = Montar(gestao);
		await adaptador.ListarAtivosAsync();

		adaptador.Esquecer();
		await adaptador.ListarAtivosAsync();

		gestao.Listagens.Should().Be(2, "sem esquecer, a segunda viria do cache");
	}

	[Fact]
	public async Task Desenvolvimento_TrazOsNomesFicticios()
	{
		var lidos = await new UsuariosParaDiretorioDeDesenvolvimento().ListarAtivosAsync();

		lidos.Value.Single(u => u.Id == PessoasDeDesenvolvimento.AnaId).Nome.Should().Be("Ana Ribeiro");
	}
}
