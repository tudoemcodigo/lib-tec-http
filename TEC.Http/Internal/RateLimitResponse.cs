using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using TEC.Core.Common.Results;
using TEC.Core.Common.Serialization;
using TEC.Core.Responses;

namespace TEC.Http.Internal;

/// <summary>Metadados JSON gerados em compilação do envelope de erro (compatível com Native AOT).</summary>
[JsonSerializable(typeof(ApiResponse))]
internal sealed partial class HttpJsonContext : JsonSerializerContext;

/// <summary>Resposta 429 no envelope padrão (<see cref="ApiResponse"/>), com <c>Retry-After</c> e <c>traceId</c>.</summary>
internal static class RateLimitResponse
{
    internal const string ErrorCode = "http.rate_limit_exceeded";
    private const string Message = "Limite de requisições excedido. Tente novamente mais tarde.";

    private static readonly JsonSerializerOptions JsonOptions = CreateOptions();

    public static Task WriteAsync(HttpContext context, RateLimitLease lease, int windowSeconds)
    {
        var response = context.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.CacheControl = "no-store";

        // Sem a estimativa do limiter, a janela inteira é um valor seguro (nunca 0, que convidaria a repetir já)
        int retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out TimeSpan wait)
            ? Math.Clamp((int)Math.Ceiling(wait.TotalSeconds), 1, windowSeconds)
            : windowSeconds;
        response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        var error = Error.TooManyRequests(ErrorCode, Message);
        var body = ApiResponse.Fail(StatusCodes.Status429TooManyRequests, Message, ApiError.FromError(error)) with
        {
            TraceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier
        };

        var typeInfo = (JsonTypeInfo<ApiResponse>)JsonOptions.GetTypeInfo(typeof(ApiResponse));
        return response.WriteAsJsonAsync(body, typeInfo, "application/json; charset=utf-8", context.RequestAborted);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = JsonDefaults.CreateOptions(HttpJsonContext.Default);
        options.MakeReadOnly();
        return options;
    }
}
