using Microsoft.Extensions.Options;
using PlayOffs.Worker;
using PlayOffs.Worker.Contracts;
using PlayOffs.Worker.Infrastructure;
using PlayOffs.Worker.Processing;
using PlayOffs.Worker.Processing.Handlers;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<WorkerOptions>(builder.Configuration.GetSection("Worker"));

builder.Services.AddSingleton<IEventInboxReader, PostgresEventInboxReader>();
builder.Services.AddSingleton<IReadModelStore, RedisReadModelStore>();
builder.Services.AddSingleton<IProjectionQueryRepository, PostgresProjectionQueryRepository>();

builder.Services.AddSingleton<IEventProjectionHandler, GoalScoredProjectionHandler>();
builder.Services.AddSingleton<IEventProjectionHandler, MatchEndedProjectionHandler>();
builder.Services.AddSingleton<IEventRouter, EventRouter>();

builder.Services.AddHostedService<WorkerService>();

var host = builder.Build();
await host.RunAsync();
