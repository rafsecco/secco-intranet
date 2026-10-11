using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Secco.Intranet.Application;
using Secco.Intranet.Application.Tenants.Acesso;
using Secco.Intranet.Infrastructure.Access;
using Secco.SecureGate.Client;
using Xunit;

namespace Secco.Intranet.Tests.Unit;

/// <summary>Adaptador do acesso do tenant administrado: tenant explícito, mapeamento do cadastro e dos 409.</summary>
public class SecureGateGestaoDeAcessoDoTenantTests
{
	private static readonly Guid Alvo = Guid.NewGuid();

	private sealed class ClientFalso() : SecureGateClient(new HttpClient())
	{
		public List<string> Chamadas { get; } = [];

		public CreateUserRequest? Criado { get; private set; }

		public Exception? Falha { get; set; }

		private Task Registrar(string chamada)
		{
			Chamadas.Add(chamada);

			return Falha is null ? Task.CompletedTask : Task.FromException(Falha);
		}

		public override async Task<ICollection<RoleDto>> ListRolesAsync(Guid tenantId, CancellationToken cancellationToken)
		{
			await Registrar($"ListRoles:{tenantId}");

			return [new RoleDto { Name = "compras-aprovador", Permissions = ["pedido:aprovar"] }];
		}

		public override async Task<UserDto> CreateUserAsync(Guid tenantId, CreateUserRequest body, CancellationToken cancellationToken)
		{
			await Registrar($"CreateUser:{tenantId}");
			Criado = body;

			return new UserDto { Id = Guid.Parse("11111111-1111-1111-1111-111111111111"), Email = body.Email };
		}

		public override async Task<UserDetailDto> GetUserAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken)
		{
			await Registrar($"GetUser:{tenantId}:{userId}");

			return new UserDetailDto
			{
				Id = userId, Email = "ana@compras.com", Status = "Active", HasPassword = false, LocalLoginEnabled = true,
				DisplayName = "Ana", Roles = ["compras-aprovador"],
			};
		}

		public override async Task DeleteRoleAsync(Guid tenantId, string role, CancellationToken cancellationToken) =>
			await Registrar($"DeleteRole:{tenantId}:{role}");

		public override async Task ResendUserInviteAsync(Guid tenantId, Guid userId, CancellationToken cancellationToken) =>
			await Registrar($"ResendUserInvite:{tenantId}:{userId}");
	}

	private static (SecureGateGestaoDeAcessoDoTenant Gestao, ClientFalso Client) Criar()
	{
		var client = new ClientFalso();

		return (new SecureGateGestaoDeAcessoDoTenant(client, NullLogger<SecureGateGestaoDeAcessoDoTenant>.Instance), client);
	}

	private static ApiException Api(int status, string corpo = "") =>
		new("falha", status, corpo, new Dictionary<string, IEnumerable<string>>(), null);

	[Fact]
	public async Task ListarPerfis_UsaOTenantRecebido()
	{
		var (gestao, client) = Criar();

		var perfis = await gestao.ListarPerfisAsync(Alvo);

		client.Chamadas.Should().Equal($"ListRoles:{Alvo}");
		perfis.Value.Should().ContainSingle().Which.Should().Be(new PerfilDoTenantDto("compras-aprovador", 1, false));
	}

	[Fact]
	public async Task Cadastrar_MontaOPedidoEDevolveOId()
	{
		var (gestao, client) = Criar();

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", "Ana", ["compras-aprovador"], EntraPorSenha: false));

		id.Value.Should().Be(Guid.Parse("11111111-1111-1111-1111-111111111111"));
		client.Chamadas.Should().Equal($"CreateUser:{Alvo}");
		client.Criado!.Email.Should().Be("ana@compras.com");
		client.Criado.LocalLogin.Should().BeFalse();
		client.Criado.DisplayName.Should().Be("Ana");
		client.Criado.Roles.Should().Equal("compras-aprovador");
	}

	[Fact]
	public async Task Cadastrar_409_ViraEmailJaCadastrado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, "{\"code\":\"SecureGate.User.AlreadyExists\"}");

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.EmailJaCadastrado);
	}

	[Fact]
	public async Task Cadastrar_400_ViraCadastroRecusado()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(400);

		var id = await gestao.CadastrarUsuarioAsync(Alvo, new NovoUsuarioDoTenant("ana@compras.com", null, [], true));

		id.Error.Should().Be(IntranetErrors.Acesso.CadastroRecusado);
	}

	[Fact]
	public async Task ObterUsuario_TrazSenhaEModoDeEntrada()
	{
		var (gestao, _) = Criar();
		var usuario = Guid.NewGuid();

		var lido = await gestao.ObterUsuarioAsync(Alvo, usuario);

		lido.Value.TemSenha.Should().BeFalse();
		lido.Value.EntraPorSenha.Should().BeTrue();
		lido.Value.Nome.Should().Be("Ana");
	}

	[Theory]
	[InlineData("{\"code\":\"SecureGate.Role.UsedByClients\"}", "Intranet.Acesso.PerfilUsadoPorClient")]
	[InlineData("{\"code\":\"SecureGate.Role.HasMembers\"}", "Intranet.Acesso.PerfilComMembros")]
	public async Task ExcluirPerfil_409_DistingueOMotivo(string corpo, string codigoEsperado)
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409, corpo);

		(await gestao.ExcluirPerfilAsync(Alvo, "x")).Error.Code.Should().Be(codigoEsperado);
	}

	[Fact]
	public async Task ReenviarConvite_409_ViraConviteNaoCabe()
	{
		var (gestao, client) = Criar();
		client.Falha = Api(409);

		(await gestao.ReenviarConviteAsync(Alvo, Guid.NewGuid())).Error.Should().Be(IntranetErrors.Acesso.ConviteNaoCabe);
	}
}
