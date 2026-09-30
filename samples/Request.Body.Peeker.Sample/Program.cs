using System.Text.Json;
using Request.Body.Peeker;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);

var app = builder.Build();

app.Use(async (context, next) =>
{
    if (!HttpMethods.IsPost(context.Request.Method) || context.Request.Path != "/orders")
    {
        await next(context);
        return;
    }

    OrderRequest? order;
    try
    {
        order = await context.Request.PeekBodyAsync<OrderRequest>(
            options: new PeekOptions { BufferLimit = 64 * 1024 },
            cancellationToken: context.RequestAborted);
    }
    catch (IOException)
    {
        await WritePlainText(context, StatusCodes.Status413PayloadTooLarge, "Request body exceeds 64 KB.");
        return;
    }
    catch (JsonException)
    {
        await WritePlainText(context, StatusCodes.Status400BadRequest, "Request body is not valid JSON.");
        return;
    }

    if (order?.TenantId != context.Request.Headers["X-Tenant"].ToString())
    {
        await WritePlainText(context, StatusCodes.Status403Forbidden, "Body tenantId does not match the X-Tenant header.");
        return;
    }

    context.Items["peeked"] = order;
    await next(context);
});

app.UseRouting();

app.MapPost("/orders", (OrderRequest body, HttpContext ctx) => Results.Json(new
{
    peekedByMiddleware = ctx.Items["peeked"],
    boundByEndpoint = body,
    contentLengthHeader = ctx.Request.ContentLength
}));

app.MapPost("/echo", async (HttpContext ctx) =>
{
    var peeked = await ctx.Request.PeekBodyAsync(cancellationToken: ctx.RequestAborted);

    using var reader = new StreamReader(ctx.Request.Body, leaveOpen: true);
    var secondRead = await reader.ReadToEndAsync(ctx.RequestAborted);

    return Results.Text($"{peeked}\n-- second read: {secondRead.Length} chars", "text/plain");
});

app.Run();

static Task WritePlainText(HttpContext context, int statusCode, string message)
{
    context.Response.StatusCode = statusCode;
    context.Response.ContentType = "text/plain; charset=utf-8";
    return context.Response.WriteAsync(message, context.RequestAborted);
}

public sealed record OrderRequest(string TenantId, string Sku, int Quantity);
