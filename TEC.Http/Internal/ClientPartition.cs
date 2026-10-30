using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;

namespace TEC.Http.Internal;

/// <summary>Chaves de partição do limite. Toda chave tem tamanho limitado, para o estado em memória não crescer sem controle.</summary>
internal static class ClientPartition
{
    /// <summary>Claim de identidade normalizada do TEC.Security (<c>TecClaimTypes.UserId</c>).</summary>
    private const string TecUserId = "tec_uid";

    private const int MaxIdentityKeyLength = 128;

    private const int MaxConnectionKeyLength = 64;

    /// <summary>
    /// Chave por IP. IPv6 é agrupado por prefixo /64 (um cliente controla todo o /64; sem isso, rotacionar o sufixo burlaria o
    /// limite). Sem IP (ex.: Unix domain socket, onde o <c>ForwardedHeaders</c> também não confia em proxy nenhum), a chave
    /// é a conexão: clientes distintos não dividem um balde só e cada conexão continua limitada. Sem IP nem id de conexão,
    /// cai em um balde compartilhado: falha fechada, nunca sem limite.
    /// </summary>
    public static string Ip(HttpContext context)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null)
            return Connection(context.Connection.Id);

        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();

        if (ip.AddressFamily != AddressFamily.InterNetworkV6)
            return "ip:" + ip;

        Span<byte> bytes = stackalloc byte[16];
        if (!ip.TryWriteBytes(bytes, out _))
            return "ip:unknown";

        return "ip6:" + Convert.ToHexString(bytes[..8]);
    }

    /// <summary>O id vem do servidor, não do cliente; mesmo assim o tamanho é limitado (hash acima do limite).</summary>
    private static string Connection(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return "ip:unknown";

        return id.Length <= MaxConnectionKeyLength
            ? "conn:" + id
            : "connh:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }

    /// <summary>Chave por identidade, ou <c>null</c> para requisição anônima (não passa pelo estágio de identidade).</summary>
    public static string? Identity(HttpContext context)
    {
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return null;

        var id = user.FindFirst(TecUserId)?.Value
            ?? user.FindFirst("sub")?.Value
            ?? user.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier")?.Value
            ?? user.Identity.Name;

        // Autenticado sem identificador estável: balde próprio (falha fechada) em vez de escapar do limite
        if (string.IsNullOrEmpty(id))
            return "id:unidentified";

        return id.Length <= MaxIdentityKeyLength
            ? "id:" + id
            : "idh:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));
    }
}
