using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

public class ControllerAspect : ActionFilterAttribute
{
    private readonly ILogger<ControllerAspect> _logger;
    private Stopwatch _stopwatch = new();

    public ControllerAspect(ILogger<ControllerAspect> logger)
    {
        _logger = logger;
    }

    public override void OnActionExecuting(
        ActionExecutingContext context)
    {
        _stopwatch.Start();

        var request = context.HttpContext.Request;

        _logger.LogInformation(
            "Request: {Method} {Path}",
            request.Method,
            request.Path
        );

        _logger.LogInformation(
            "Controller: {Controller}, Action: {Action}",
            context.RouteData.Values["controller"],
            context.RouteData.Values["action"]
        );

        base.OnActionExecuting(context);
    }

    public override void OnActionExecuted(
        ActionExecutedContext context)
    {
        _stopwatch.Stop();

        var response = context.HttpContext.Response;

        _logger.LogInformation(
            "Response: {StatusCode}, Duration: {Elapsed} ms",
            response.StatusCode,
            _stopwatch.ElapsedMilliseconds
        );

        base.OnActionExecuted(context);
    }
}