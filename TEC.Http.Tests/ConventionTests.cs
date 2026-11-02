using System.Diagnostics.Metrics;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TEC.Http.Internal;

namespace TEC.Http.Tests;

/// <summary>Trava as convenções de telemetria da Doutrina: faixa de EventId (CMP-OBS-07) e nome de instrumento (CMP-OBS-06).</summary>
public class ConventionTests
{
    private const int FirstEventId = 6000;
    private const int LastEventId = 6999;

    [Test]
    public async Task Todo_EventId_do_componente_esta_na_faixa_6000_a_6999()
    {
        var ids = typeof(TecHttpLog).Assembly.GetTypes()
            .SelectMany(t => t.GetMethods(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            .Select(m => m.GetCustomAttribute<LoggerMessageAttribute>())
            .OfType<LoggerMessageAttribute>()
            .Select(a => a.EventId)
            .ToList();

        await Assert.That(ids).IsNotEmpty();
        await Assert.That(ids.All(id => id is >= FirstEventId and <= LastEventId)).IsTrue();
        await Assert.That(ids.Distinct().Count()).IsEqualTo(ids.Count);
    }

    [Test]
    public async Task Todo_instrumento_comeca_com_tec_http_e_o_medidor_nao_muda()
    {
        using var provider = new ServiceCollection().AddMetrics().BuildServiceProvider();
        var names = new List<string>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, _) =>
            {
                if (instrument.Meter.Name == TecHttpMetrics.MeterName)
                {
                    names.Add(instrument.Name);
                }
            }
        };
        listener.Start();

        _ = new TecHttpMetrics(provider.GetRequiredService<IMeterFactory>());

        await Assert.That(TecHttpMetrics.MeterName).IsEqualTo("TEC.Http");
        await Assert.That(names).IsNotEmpty();
        await Assert.That(names.All(n => n.StartsWith("tec.http.", StringComparison.Ordinal) && n == n.ToLowerInvariant())).IsTrue();
    }
}
