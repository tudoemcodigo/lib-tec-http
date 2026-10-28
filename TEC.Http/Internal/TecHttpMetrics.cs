using System.Diagnostics.Metrics;

namespace TEC.Http.Internal;

/// <summary>Métricas do componente (medidor <c>TEC.Http</c>).</summary>
internal sealed class TecHttpMetrics
{
    /// <summary>Nome do medidor.</summary>
    public const string MeterName = "TEC.Http";

    private readonly Counter<long> _rejected;
    private readonly Counter<long> _forwardedIgnored;

    public TecHttpMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _rejected = meter.CreateCounter<long>("tec.http.ratelimit.rejected", description: "Requisições recusadas pelo limite (429).");
        _forwardedIgnored = meter.CreateCounter<long>("tec.http.forwarded_headers.ignored",
            description: "Requisições com X-Forwarded-For recebidas sem Http:Forwarding habilitado (header ignorado).");
    }

    public void Rejected(string stage) => _rejected.Add(1, new KeyValuePair<string, object?>("stage", stage));

    public void ForwardedIgnored() => _forwardedIgnored.Add(1);
}
