using Microsoft.EntityFrameworkCore;
using TopUpService.API.Middlewares;
using TopUpService.Application.Interfaces;
using TopUpService.Application.Services;
using TopUpService.Infrastructure.Idempotency;
using TopUpService.Infrastructure.MciClient;
using TopUpService.Infrastructure.Messaging;
using TopUpService.Infrastructure.Messaging.Consumers;
using TopUpService.Infrastructure.Messaging.Publishers;
using TopUpService.Infrastructure.Persistence;
using TopUpService.Infrastructure.Persistence.Repositories;

var builder = WebApplication.CreateBuilder(args);

// ── Configuration ─────────────────────────────────────────────────────────────
builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

// ── Database (SQLite for demo) ────────────────────────────────────────────────
builder.Services.AddDbContext<TopUpDbContext>(options =>
    options.UseSqlite(
        builder.Configuration.GetConnectionString("TopUpDb") ?? "Data Source=topup.db"));

// ── Domain / Application ──────────────────────────────────────────────────────
builder.Services.AddScoped<ITopUpRepository, TopUpRepository>();
builder.Services.AddScoped<ProcessTopUpCommandHandler>();

// ── Infrastructure ────────────────────────────────────────────────────────────
// MCI Client: swap MockMciTopUpClient with a real HTTP implementation for production.
builder.Services.AddSingleton<IMciTopUpClient, MockMciTopUpClient>();

// Idempotency: swap InMemoryIdempotencyStore with a Redis-backed store for production.
builder.Services.AddSingleton<IIdempotencyStore, InMemoryIdempotencyStore>();

// RabbitMQ publisher (Singleton — one persistent channel)
builder.Services.AddSingleton<ITopUpResultPublisher, RabbitMqTopUpResultPublisher>();

// RabbitMQ consumer (BackgroundService — starts with the host)
builder.Services.AddHostedService<TopUpRequestConsumer>();

// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "TopUp Service API",
        Version = "v1",
        Description = "Async TopUp service for MCI operator. Primary flow is RabbitMQ-driven; HTTP exposes operational queries."
    });
});

var app = builder.Build();

// Auto-apply EF schema at startup (use explicit migrations in production).
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<TopUpDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.UseMiddleware<GlobalExceptionMiddleware>();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/swagger/v1/swagger.json", "TopUp Service v1"));
}

app.MapControllers();

app.Run();
