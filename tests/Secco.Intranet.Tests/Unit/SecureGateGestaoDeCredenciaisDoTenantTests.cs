using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Credenciais;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Adaptador das credenciais do tenant administrado: tenant explícito, tipos e tradução de erros.</summary>
public class SecureGateGestaoDeCredenciaisDoTenantTests
{
	private static readonly Guid Alvo = Guid.NewGuid();
	private const string IdMaquina = "cli_abcdefghijklmnop";
	private const string IdLogin = "cli_qrstuvwxyz234567";
	private const string SecretEmitido = "segredo-emitido-9f3a";
	private const string SecretNovo = "segredo-novo-71bc";

	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public ProductClientRequest? Pedido { get; private set; }

		public Exception? Falha { get; set; }

		public ICollection<ProductClientDto> Existentes { get; set; } = [];

		public string? TipoDevolvido { get; set; } = "client_credentials";

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<CreatedProductClientDto> CreateProductClientAsync(
			Guid tenantId, ProductClientRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"Create:{tenantId}");
			Pedido = body;

			return new CreatedProductClientDto
			{
				ClientId = IdMaquina, Name = body.Name, Scopes = body.Scopes, Roles = body.Roles, Type = body.Type,
				RedirectUris = body.RedirectUris, PostLogoutRedirectUris = body.PostLogoutRedirectUris, ClientSecret = SecretEmitido,
			};
		}

		public override async Task<ICollection<ProductClientDto>> ListProductClientsAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"List:{tenantId}");

			return Existentes;
		}

		public override async Task<ProductClientDto> GetProductClientAsync(Guid tenantId, string clientId, CancellationToken cancellationToken)
		{
			await Registrar($"Get:{tenantId}:{clientId}");

			return new ProductClientDto { ClientId = clientId, Name = "ERP", Scopes = ["logstream"], Roles = ["leitor"], Type = TipoDevolvido };
		}

		public override async Task UpdateProductClientAsync(
			Guid tenantId, string clientId, ProductClientRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"Update:{tenantId}:{clientId}");
			Pedido = body;
		}

		public override async Task<ProductClientSecretDto> RotateProductClientSecretAsync(
			Guid tenantId, string clientId, CancellationToken cancellationToken)
		{
			await Registrar($"Rotate:{tenantId}:{clientId}");

			return new ProductClientSecretDto { ClientId = clientId, ClientSecret = SecretNovo };
		}

		public override async Task DeleteProductClientAsync(Guid tenantId, string clientId, CancellationToken cancellationToken) =>
			await Registrar($"Delete:{tenantId}:{clientId}");
	}

	private sealed class LoggerQueGuarda : ILogger<SecureGateGestaoDeCredenciaisDoTenant>
	{
		public List<string> Mensagens { get; } = [];

		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull => null;

		public bool IsEnabled(LogLevel logLevel) => true;

		public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
			Mensagens.Add(formatter(state, exception));
	}

	private static (SecureGateGestaoDeCredenciaisDoTenant Gestao, ClientFalso Client, LoggerQueGuarda Log) Criar()
	{
		var client = new ClientFalso();
		var log = new LoggerQueGuarda();

		return (new SecureGateGestaoDeCredenciaisDoTenant(client, log), client, log);
	}

	private static ApiException Problema(int status, string titulo, string? detalhe = null) =>
		new ApiException<ProblemDetails>(
			"x", status, null!, new Dictionary<string, IEnumerable<string>>(), new ProblemDetails { Title = titulo, Detail = detalhe }, null);

	private static DadosDaCredencial Maquina() =>
		new(TipoDeCredencial.Maquina, "ERP", ["logstream"], ["leitor"], [], []);

	private static DadosDaCredencial Login() =>
		new(TipoDeCredencial.Login, "Portal", ["logstream"], ["ignorado"], ["https://portal.exemplo.com/cb"], ["https://portal.exemplo.com/fim"]);

	[Fact]
	public async Task Emitir_UsaOTenantRecebido_EMapeiaMaquina()
	{
		var (gestao, client, _) = Criar();

		var emitida = await gestao.EmitirAsync(Alvo, Maquina());

		client.Chamadas.Should().Equal($"Create:{Alvo}");
		client.Pedido!.Type.Should().Be("client_credentials");
		client.Pedido.RedirectUris.Should().BeNull();
		client.Pedido.PostLogoutRedirectUris.Should().BeNull();
		client.Pedido.Roles.Should().Equal("leitor");
		emitida.Value.Secret.Valor.Should().Be(SecretEmitido);
		emitida.Value.Secret.ClientId.Should().Be(IdMaquina);
		emitida.Value.Credencial.Tipo.Should().Be(TipoDeCredencial.Maquina);
	}

	[Fact]
	public async Task Emitir_Login_MandaAuthorizationCodeERolesVazio()
	{
		var (gestao, client, _) = Criar();

		var emitida = await gestao.EmitirAsync(Alvo, Login());

		client.Pedido!.Type.Should().Be("authorization_code");
		client.Pedido.Roles.Should().BeEmpty();
		client.Pedido.RedirectUris.Should().Equal("https://portal.exemplo.com/cb");
		client.Pedido.PostLogoutRedirectUris.Should().Equal("https://portal.exemplo.com/fim");
		emitida.Value.Credencial.Tipo.Should().Be(TipoDeCredencial.Login);
		emitida.Value.Credencial.RedirectUris.Should().Equal("https://portal.exemplo.com/cb");
	}

	[Fact]
	public async Task Alterar_UsaOTenantEOClientRecebidos()
	{
		var (gestao, client, _) = Criar();

		var alterada = await gestao.AlterarAsync(Alvo, IdLogin, Login());

		alterada.IsSuccess.Should().BeTrue();
		client.Chamadas.Should().Equal($"Update:{Alvo}:{IdLogin}");
		client.Pedido!.Type.Should().Be("authorization_code");
	}

	[Theory]
	[InlineData(null, TipoDeCredencial.Maquina)]
	[InlineData("client_credentials", TipoDeCredencial.Maquina)]
	[InlineData("authorization_code", TipoDeCredencial.Login)]
	public async Task Obter_MapeiaOTipoDaPlataforma(string? tipo, TipoDeCredencial esperado)
	{
		var (gestao, client, _) = Criar();
		client.TipoDevolvido = tipo;

		var credencial = await gestao.ObterAsync(Alvo, IdMaquina);

		client.Chamadas.Should().Equal($"Get:{Alvo}:{IdMaquina}");
		credencial.Value.Tipo.Should().Be(esperado);
		credencial.Value.Perfis.Should().Equal("leitor");
	}

	[Fact]
	public async Task Obter_TipoDesconhecido_Indisponivel()
	{
		var (gestao, client, log) = Criar();
		client.TipoDevolvido = "implicit";

		var credencial = await gestao.ObterAsync(Alvo, IdMaquina);

		credencial.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
		log.Mensagens.Should().ContainSingle().Which.Should().Contain(IdMaquina).And.NotContain("ERP");
	}

	[Fact]
	public async Task Listar_OmiteTipoDesconhecido()
	{
		var (gestao, client, log) = Criar();
		client.Existentes =
		[
			new ProductClientDto { ClientId = IdMaquina, Name = "ERP", Type = "client_credentials" },
			new ProductClientDto { ClientId = IdLogin, Name = "Estranho", Type = "implicit" },
		];

		var lista = await gestao.ListarAsync(Alvo);

		lista.Value.Should().ContainSingle().Which.ClientId.Should().Be(IdMaquina);
		log.Mensagens.Should().ContainSingle().Which.Should().Contain(IdLogin).And.NotContain("Estranho");
	}

	[Theory]
	[InlineData(404, "SecureGate.Qualquer.Codigo", "Intranet.Credenciais.NaoEncontrada")]
	[InlineData(409, "SecureGate.Client.NameAlreadyExists", "Intranet.Credenciais.NomeEmUso")]
	[InlineData(409, "SecureGate.Client.TenantInactive", "Intranet.Credenciais.TenantDesativado")]
	[InlineData(400, "SecureGate.Client.RoleNotFound", "Intranet.Credenciais.PerfilRecusado")]
	[InlineData(400, "SecureGate.Client.RoleNotAssignable", "Intranet.Credenciais.PerfilRecusado")]
	[InlineData(400, "SecureGate.Client.RedirectUrisInvalid", "Intranet.Credenciais.EnderecoRecusado")]
	[InlineData(400, "SecureGate.Client.PostLogoutRedirectUrisInvalid", "Intranet.Credenciais.EnderecoRecusado")]
	public async Task Emitir_TraduzOErroPeloTitulo(int status, string titulo, string codigoEsperado)
	{
		var (gestao, client, _) = Criar();
		client.Falha = Problema(status, titulo);

		var emitida = await gestao.EmitirAsync(Alvo, Maquina());

		emitida.Error.Code.Should().Be(codigoEsperado);
	}

	[Fact]
	public async Task Alterar_TraduzOMesmoMapa()
	{
		var (gestao, client, _) = Criar();
		client.Falha = Problema(409, "SecureGate.Client.NameAlreadyExists");

		var alterada = await gestao.AlterarAsync(Alvo, IdMaquina, Maquina());

		alterada.Error.Should().Be(IntranetErrors.Credenciais.NomeEmUso);
	}

	[Fact]
	public async Task Recusa400Generica_LevaODetail()
	{
		var (gestao, client, _) = Criar();
		client.Falha = Problema(400, "SecureGate.Client.Outro", "Escopo nao permitido.");

		var emitida = await gestao.EmitirAsync(Alvo, Maquina());

		emitida.Error.Should().Be(IntranetErrors.Credenciais.Recusada("Escopo nao permitido."));
	}

	[Fact]
	public async Task FalhaDeRede_Indisponivel()
	{
		var (gestao, client, _) = Criar();
		client.Falha = new HttpRequestException("sem rede");

		var lista = await gestao.ListarAsync(Alvo);

		lista.Error.Should().Be(IntranetErrors.Acesso.Indisponivel);
	}

	[Fact]
	public async Task Rotacionar_DevolveSecretNovo()
	{
		var (gestao, client, log) = Criar();

		var secret = await gestao.RotacionarSecretAsync(Alvo, IdMaquina);

		client.Chamadas.Should().Equal($"Rotate:{Alvo}:{IdMaquina}");
		secret.Value.Valor.Should().Be(SecretNovo);
		secret.Value.ClientId.Should().Be(IdMaquina);
		log.Mensagens.Should().NotContain(m => m.Contains(SecretNovo));
	}

	[Fact]
	public async Task Revogar_ChamaDelete()
	{
		var (gestao, client, _) = Criar();

		var revogada = await gestao.RevogarAsync(Alvo, IdMaquina);

		revogada.IsSuccess.Should().BeTrue();
		client.Chamadas.Should().Equal($"Delete:{Alvo}:{IdMaquina}");
	}
}
