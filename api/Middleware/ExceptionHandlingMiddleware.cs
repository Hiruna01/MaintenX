using System.Diagnostics;
using CampusFacilities.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace CampusFacilities.Api.Middleware;

/// <summary>
/// Last line of defence: turns any unhandled exception into a ProblemDetails 500 so
/// clients always get JSON, never an HTML error page or a raw stack trace.
///
/// One exception is not a fault: an illegal workflow transition is a 409, from whichever
/// service tried it (see WorkflowTransitions). A transaction open around the move has
/// already rolled back by the time it arrives here, so the 409 means nothing was written.
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
        catch (InvalidWorkflowTransitionException ex) when (!context.Response.HasStarted)
        {
            // Not a fault in this API: the workflow is somewhere the caller did not expect,
            // and saying so is the state machine doing its job. A warning, not an error.
            _logger.LogWarning("Refused {Method} {Path}: {Reason}",
                context.Request.Method, context.Request.Path, ex.Message);

            await WriteProblemAsync(context, new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Illegal workflow transition",
                Detail = ex.Message,
                Type = "https://tools.ietf.org/html/rfc9110#section-15.5.10",
                Instance = context.Request.Path
            });
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

            await WriteProblemAsync(context, problem);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, ProblemDetails problem)
    {
        context.Response.Clear();
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        // contentType must be passed here — WriteAsJsonAsync otherwise overwrites it
        // with application/json and the response stops being a ProblemDetails document.
        await context.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json");
    }
}
