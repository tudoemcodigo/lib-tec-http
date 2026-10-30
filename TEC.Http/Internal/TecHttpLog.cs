using Microsoft.Extensions.Logging;

namespace TEC.Http.Internal;

internal static partial class TecHttpLog
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Warning,
        Message = "Requisição com X-Forwarded-For recebida com Http:Forwarding desligado: o header foi ignorado e o IP é o da conexão. " +
                  "Se o serviço está atrás de proxy reverso, configure Http:Forwarding (KnownProxies/KnownNetworks); " +
                  "senão o limite por IP trata todos os clientes como o IP do proxy.")]
    public static partial void ForwardedHeaderIgnored(ILogger logger);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning,
        Message = "Requisição sem IP remoto (ex.: Unix domain socket): o limite por IP passou a valer por conexão. " +
                  "Atrás de proxy por socket Unix, o limite efetivo depende de quantas conexões o proxy abre; " +
                  "limite também no proxy ou use TCP com Http:Forwarding.")]
    public static partial void RemoteIpUnknown(ILogger logger);
}
