using ACA360.Logging.Interfaces;
using Microsoft.AspNetCore.Http;

public class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;

    public GlobalExceptionMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context, ILoggerService logger)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                context.Request.Path,
                "Unhandled Exception",
                $"Method={context.Request.Method}",
                context.Connection.RemoteIpAddress?.ToString() ?? "Unknown"
            );

            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Internal Server Error");
        }
    }
}
