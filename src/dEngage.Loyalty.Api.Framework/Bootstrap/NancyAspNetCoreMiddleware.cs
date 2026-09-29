using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Nancy;

namespace dEngage.Loyalty.Api.Framework.Bootstrap;

// The custom ASP.NET Core <-> Nancy bridge referenced in the plan (§1): translates HttpContext into
// a Nancy Request, runs it through the Nancy engine, translates the Response back. Written by hand
// instead of depending on the Nancy.Owin package, which only restores against net8.0 via a
// .NETFramework compatibility shim (NU1701) — see the plan's ground-truth risk note. Everything
// outside "/api" is passed through to the rest of the ASP.NET Core pipeline (health checks, etc.).
public sealed class NancyAspNetCoreMiddleware(RequestDelegate next, INancyEngine engine, IServiceScopeFactory scopeFactory)
{
    public async Task InvokeAsync(HttpContext httpContext)
    {
        if (!httpContext.Request.Path.StartsWithSegments("/api"))
        {
            await next(httpContext);
            return;
        }

        using var scope = scopeFactory.CreateScope();
        AmbientServiceProviderAccessor.Current = scope.ServiceProvider;
        try
        {
            var nancyRequest = BuildNancyRequest(httpContext);
            using var nancyContext = await engine.HandleRequest(nancyRequest, ctx => ctx, httpContext.RequestAborted);
            await WriteResponseAsync(httpContext, nancyContext.Response);
        }
        finally
        {
            AmbientServiceProviderAccessor.Current = null;
        }
    }

    private static Request BuildNancyRequest(HttpContext httpContext)
    {
        var req = httpContext.Request;

        var url = new Url
        {
            Scheme = req.Scheme,
            HostName = req.Host.Host,
            Port = req.Host.Port,
            BasePath = "",
            Path = req.Path.Value ?? "/",
            Query = req.QueryString.Value ?? ""
        };

        var headers = req.Headers.ToDictionary(
            h => h.Key,
            h => (IEnumerable<string>)h.Value.Select(v => v ?? "").ToArray());

        return new Request(
            method: req.Method,
            url: url,
            body: req.Body,
            headers: headers,
            ip: httpContext.Connection.RemoteIpAddress?.ToString() ?? "",
            certificate: null,
            protocolVersion: req.Protocol);
    }

    private static async Task WriteResponseAsync(HttpContext httpContext, Response nancyResponse)
    {
        httpContext.Response.StatusCode = (int)nancyResponse.StatusCode;
        if (!string.IsNullOrEmpty(nancyResponse.ContentType))
            httpContext.Response.ContentType = nancyResponse.ContentType;

        foreach (var header in nancyResponse.Headers)
            httpContext.Response.Headers[header.Key] = header.Value;

        // Buffer first: Kestrel disallows synchronous writes on the response body by default, and
        // Nancy's Response.Contents is a synchronous Action<Stream>.
        using var buffer = new MemoryStream();
        nancyResponse.Contents(buffer);
        buffer.Position = 0;
        await buffer.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
    }
}
