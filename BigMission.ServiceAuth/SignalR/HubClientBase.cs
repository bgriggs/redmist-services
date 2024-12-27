﻿using BigMission.Shared.Auth;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BigMission.Shared.SignalR;

/// <summary>
/// Base class for SignalR hub clients. This class handles the connection, reconnection, and authentication logic.
/// </summary>
public abstract class HubClientBase : BackgroundService
{
    private readonly IConfiguration configuration;
    private ILogger Logger { get; }

    protected virtual TimeSpan ReconnectDelay => TimeSpan.FromSeconds(5);

    public HubClientBase(ILoggerFactory loggerFactory, IConfiguration configuration)
    {
        Logger = loggerFactory.CreateLogger(GetType().Name);
        this.configuration = configuration;
    }

    protected virtual HubConnection GetConnection()
    {
        var url = configuration["Hub:Url"] ?? throw new InvalidOperationException("Hub URL is not configured.");
        var authUrl = configuration["Keycloak:AuthServerUrl"] ?? throw new InvalidOperationException("Keycloak URL is not configured.");
        var realm = configuration["Keycloak:Realm"] ?? throw new InvalidOperationException("Keycloak realm is not configured.");
        var clientId = configuration["Keycloak:ClientId"] ?? throw new InvalidOperationException("Keycloak client ID is not configured.");
        var clientSecret = configuration["Keycloak:ClientSecret"] ?? throw new InvalidOperationException("Keycloak client secret is not configured.");

        // Log all parameters
        Logger.LogDebug($"Hub URL: {url}");
        Logger.LogDebug($"Keycloak Auth URL: {authUrl}");
        Logger.LogDebug($"Keycloak Realm: {realm}");
        Logger.LogDebug($"Keycloak Client ID: {clientId}");
        Logger.LogDebug($"Keycloak Client Secret: {new string('*', clientSecret.Length)}");

        var builder = new HubConnectionBuilder()
            .WithUrl(url, options => options.AccessTokenProvider = async () => await KeycloakServiceToken.RequestClientToken(authUrl, realm, clientId, clientSecret))
            .WithAutomaticReconnect();

        var connection = builder.Build();
        InitializeStateLogging(connection);
        return connection;
    }

    protected virtual void InitializeStateLogging(HubConnection connection)
    {
        connection.Reconnected += msg =>
        {
            Logger.LogInformation($"Hub connected: {msg}");
            return Task.CompletedTask;
        };
        connection.Closed += ex =>
        {
            Logger.LogWarning($"Connection closed: {ex?.Message}");
            return Task.CompletedTask;
        };
        connection.Reconnecting += ex =>
        {
            Logger.LogWarning($"Connection reconnecting: {ex?.Message}");
            return Task.CompletedTask;
        };
    }

    /// <summary>
    /// Call to connect to the server. Once connected, the hub will automatically reconnect if the connection is lost.
    /// </summary>
    /// <param name="connection"></param>
    /// <param name="stoppingToken"></param>
    protected virtual HubConnection StartConnection(CancellationToken stoppingToken = default)
    {
        var connection = GetConnection();
        _ = Task.Run(async () =>
        {
            bool firstTime = false;
            while (!stoppingToken.IsCancellationRequested && !firstTime)
            {
                try
                {
                    // Retry starting the initial connection to the hub
                    if (connection.State == HubConnectionState.Disconnected)
                    {
                        Logger.LogInformation("Connecting to hub...");
                        await connection.StartAsync(stoppingToken);
                        firstTime = true;
                        Logger.LogInformation($"Connected to hub: {connection.ConnectionId}");
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(ex, $"Error connecting to hub: {ex.Message}");
                }

                await Task.Delay(ReconnectDelay, stoppingToken);
            }
        }, stoppingToken);

        return connection;
    }
}
