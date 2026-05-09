namespace TopUpService.Infrastructure.Messaging;

/// <summary>
/// Strongly-typed settings for RabbitMQ, bound from appsettings.json.
/// </summary>
public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string VirtualHost { get; set; } = "/";

    // Queues
    public string RequestQueueName { get; set; } = "topup.request";
    public string ResultQueueName { get; set; } = "topup.result";

    // Exchange
    public string ExchangeName { get; set; } = "topup";
}
