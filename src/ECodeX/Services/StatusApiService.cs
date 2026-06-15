using System.Text.Json;
using ECodeX.Core.IPC.V2;

namespace ECodeX.Services;

public sealed class StatusApiService
{
    private static readonly HashSet<string> SupportedMethods = new(StringComparer.Ordinal)
    {
        "status",
        "health",
    };

    private readonly Func<string> _statusProvider;

    public StatusApiService(Func<string> statusProvider)
    {
        _statusProvider = statusProvider;
    }

    public static bool CanHandle(string? method)
    {
        return method != null && SupportedMethods.Contains(method);
    }

    public V2Response HandleRequest(V2Request request)
    {
        return request.Method switch
        {
            "status" => V2Response.FromResult(request.Id, ReadStatus()),
            "health" => V2Response.FromResult(request.Id, CreateHealthResult()),
            _ => V2Response.FromStableError(
                request.Id,
                V2ErrorCodes.NotSupported,
                $"ecodex.v2 status method is not supported: {request.Method}"),
        };
    }

    private object CreateHealthResult()
    {
        var status = ReadStatus();
        return new
        {
            ok = true,
            status,
            checks = new[]
            {
                new
                {
                    name = "status",
                    ok = true,
                    message = "Status provider responded.",
                },
            },
        };
    }

    private JsonElement ReadStatus()
    {
        using var document = JsonDocument.Parse(_statusProvider());
        return document.RootElement.Clone();
    }
}
