using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TopUpService.Application.Events;
using TopUpService.Application.Interfaces;

namespace TopUpService.Infrastructure.Messaging.Publishers;

/// <summary>
/// Publishes <see cref="TopUpResultEvent"/> messages to the RabbitMQ result queue.
/// Switch.Net consumes this queue to decide whether to confirm or reverse the
/// original Shaparak purchase transaction.
/// </summary>
public sealed class RabbitMqTopUpResultPublisher : ITopUpResultPublisher, IDisposable
{
    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqTopUpResultPublisher> _logger;

    public RabbitMqTopUpResultPublisher(
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqTopUpResultPublisher> logger)
    {
        _options = options.Value;
        _logger = logger;

        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost
        };

        _connection = factory.CreateConnection("topup-result-publisher");
        _channel = _connection.CreateModel();

        // Declare the result queue as durable so messages survive broker restarts.
        _channel.QueueDeclare(
            queue: _options.ResultQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        _logger.LogInformation(
            "RabbitMqTopUpResultPublisher connected to {Host}:{Port}", _options.Host, _options.Port);
    }

    public Task PublishAsync(TopUpResultEvent result, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(result);
        var body = Encoding.UTF8.GetBytes(json);

        var props = _channel.CreateBasicProperties();
        props.Persistent = true;                            // Survive broker restart
        props.ContentType = "application/json";
        props.CorrelationId = result.IdempotencyKey;        // Easy tracing in Switch

        _channel.BasicPublish(
            exchange: string.Empty,                         // Default exchange → route by queue name
            routingKey: _options.ResultQueueName,
            basicProperties: props,
            body: body);

        _logger.LogInformation(
            "Published TopUpResult to queue '{Queue}'. IdempotencyKey={Key} IsSuccess={Ok} RequiresReversal={Rev}",
            _options.ResultQueueName,
            result.IdempotencyKey,
            result.IsSuccess,
            result.RequiresReversal);

        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _channel.Dispose();
        _connection.Dispose();
    }
}
