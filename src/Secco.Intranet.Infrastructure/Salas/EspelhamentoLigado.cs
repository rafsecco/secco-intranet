using Secco.Intranet.Application.Salas;

namespace Secco.Intranet.Infrastructure.Salas;

/// <summary>Espelhamento ligado: há um provedor de agenda configurado neste ambiente.</summary>
internal sealed class EspelhamentoLigado : IEspelhamentoLigado
{
	/// <inheritdoc />
	public bool Ligado => true;
}
