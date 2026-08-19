using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Middleware;

/// <summary>
/// Last line of defence: turns any unhandled exception into a ProblemDetails 500 so
/// clients always get JSON, never an HTML error page or a raw stack trace.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly IHostEnvironment _environment;

    public ExceptionHandlingMiddleware(
        RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger,
        IHostEnvironment environment)
    {
        _next = next;
        _logger = logger;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception for {Method} {Path}.",
                context.Request.Method, context.Request.Path);

            if (context.Response.HasStarted)
            {
                // Too late to replace the response; let it fail as-is.
                throw;
            }

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Type = "https://tools.ietf.org/html/rfc9110#section-15.6.1",
                Instance = context.Request.Path
            };

            problem.Extensions["traceId"] = Activity.Current?.Id ?? context.TraceIdentifier;

            // Detail only in Development — production clients get no internals.
            if (_environment.IsDevelopment())
            {
                problem.Detail = ex.ToString();
            }

            context.Response.Clear();
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            // contentType must be passed here — WriteAsJsonAsync otherwise overwrites it
            // with application/json and the response stops being a ProblemDetails document.
            await context.Response.WriteAsJsonAsync(
                problem,
                options: null,
                contentType: "application/problem+json");
        }
    }
}
