using Microsoft.AspNetCore.Http.HttpResults;
using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();
builder.Services.AddSingleton<AtlasEventHub>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.MapGet("/events/next", async Task<Results<Ok<AtlasEvent>, NoContent>> (
    string clientId,
    AtlasEventHub eventHub,
    CancellationToken cancellationToken) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    timeout.CancelAfter(TimeSpan.FromSeconds(20));

    try
    {
        var atlasEvent = await eventHub.ReadAsync(clientId, timeout.Token);
        return TypedResults.Ok(atlasEvent);
    }
    catch (OperationCanceledException)
    {
        return TypedResults.NoContent();
    }
})
.WithName("WaitForEvent")
.WithSummary("Wait for the next Atlas event")
.WithDescription("Long-polls for up to 20 seconds and returns 204 when no event arrives. Each clientId receives every event published after it first connects.")
.Produces<AtlasEvent>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status204NoContent);

app.MapPost("/events", Task<Accepted<AtlasEvent>> (
    PublishEventRequest request,
    AtlasEventHub eventHub) =>
{
    var atlasEvent = new AtlasEvent(request.Type, request.Data.Clone(), DateTimeOffset.UtcNow);
    eventHub.Publish(atlasEvent);
    return Task.FromResult(TypedResults.Accepted("/events/next", atlasEvent));
})
.WithName("PublishEvent")
.WithSummary("Publish an Atlas event")
.WithDescription("Queues an event for every client that has connected with a clientId.")
.Produces<AtlasEvent>(StatusCodes.Status202Accepted);

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}

/// <summary>Payload used to publish an event to a waiting Atlas client.</summary>
public sealed record PublishEventRequest(string Type, JsonElement Data);

/// <summary>An event delivered to an Atlas client through long polling.</summary>
public sealed record AtlasEvent(string Type, JsonElement Data, DateTimeOffset PublishedAt);

public sealed class AtlasEventHub
{
    private readonly ConcurrentDictionary<string, Channel<AtlasEvent>> clients = new();

    public ValueTask<AtlasEvent> ReadAsync(string clientId, CancellationToken cancellationToken)
    {
        var clientEvents = clients.GetOrAdd(clientId, _ => Channel.CreateUnbounded<AtlasEvent>());
        return clientEvents.Reader.ReadAsync(cancellationToken);
    }

    public void Publish(AtlasEvent atlasEvent)
    {
        foreach (var clientEvents in clients.Values)
        {
            clientEvents.Writer.TryWrite(atlasEvent);
        }
    }
}
