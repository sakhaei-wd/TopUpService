using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TopUpService.Application.Commands;
using TopUpService.Application.Services;

namespace TopUpService.Infrastructure.Messaging.Consumers;

/// <summary>
/// Background service that listens to the <c>topup.request</c> queue and
/// dispatches each message to <see cref="ProcessTopUpCommandHandler"/>.
///
/// Uses a scoped DI scope per message to ensure EF Core DbContext is short-lived.
/// </summary>
public sealed class TopUpRequestConsumer : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<TopUpRequestConsumer> _logger;

    private IConnection? _connection;
    private IModel? _channel;

    public TopUpRequestConsumer(
        IServiceScopeFactory scopeFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<TopUpRequestConsumer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        ConnectWithRetry(stoppingToken);
        return Task.CompletedTask; // Consumer runs on RabbitMQ callbacks.
    }

    private void ConnectWithRetry(CancellationToken ct)
    {
        // Simple connect; in production use Polly for reconnect resilience.
        var factory = new ConnectionFactory
        {
            HostName = _options.Host,
            Port = _options.Port,
            UserName = _options.Username,
            Password = _options.Password,
            VirtualHost = _options.VirtualHost,
            DispatchConsumersAsync = true
        };

        _connection = factory.CreateConnection("topup-request-consumer");
        _channel = _connection.CreateModel();

        // Declare the request queue as durable.
        _channel.QueueDeclare(
            queue: _options.RequestQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null);

        // Process only one message at a time per consumer instance.
        // Adjust prefetchCount based on desired concurrency.
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnMessageReceivedAsync;

        _channel.BasicConsume(
            queue: _options.RequestQueueName,
            autoAck: false,         // Manual ack — message re-queued on crash
            consumer: consumer);

        _logger.LogInformation(
            "TopUpRequestConsumer listening on queue '{Queue}'", _options.RequestQueueName);
    }

    private async Task OnMessageReceivedAsync(object sender, BasicDeliverEventArgs args)
    {
        using var scope = _scopeFactory.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ProcessTopUpCommandHandler>();

        ProcessTopUpCommand? command = null;

        try
        {
            var json = Encoding.UTF8.GetString(args.Body.Span);
            command = JsonSerializer.Deserialize<ProcessTopUpCommand>(json);

            if (command is null)
            {
                _logger.LogError(
                    "Could not deserialize TopUp message. DeliveryTag={Tag} Body={Body}",
                    args.DeliveryTag, json);
                // Dead-letter the malformed message.
                _channel!.BasicNack(args.DeliveryTag, multiple: false, requeue: false);
                return;
            }

            await handler.HandleAsync(command);
            _channel!.BasicAck(args.DeliveryTag, multiple: false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Processing cancelled for DeliveryTag={Tag}", args.DeliveryTag);
            _channel?.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unhandled error processing TopUp message. DeliveryTag={Tag} IdempotencyKey={Key}",
                args.DeliveryTag, command?.IdempotencyKey ?? "unknown");

            // Requeue for retry; consider a dead-letter exchange after N failures.
            _channel?.BasicNack(args.DeliveryTag, multiple: false, requeue: true);
        }
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        _connection?.Dispose();
        base.Dispose();
    }
}
