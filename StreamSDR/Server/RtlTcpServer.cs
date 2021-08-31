using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace StreamSDR.Server
{
    /// <summary>
    /// Provides a server for SDR client applications using the rtl_tcp protocol.
    /// </summary>
    internal class RtlTcpServer : IHostedService
    {
        #region Private fields
        /// <summary>
        /// The logger.
        /// </summary>
        private readonly ILogger _logger;

        /// <summary>
        /// Cancellation token used to signal when the server should stop.
        /// </summary>
        private readonly CancellationTokenSource _serverCancellationToken = new();

        /// <summary>
        /// The TCP listener that clients connect to.
        /// </summary>
        private TcpListener? _listener;

        /// <summary>
        /// The worker thread used to process client connections.
        /// </summary>
        private readonly Thread _listenerThread;

        /// <summary>
        /// A list of the current connections to clients.
        /// </summary>
        private readonly List<RtlTcpConnection> _connections = new();
        #endregion

        #region Constructor and lifetime methods
        /// <summary>
        /// Initialises a new instance of the <see cref="RtlTcpServer"/> class.
        /// </summary>
        /// <param name="logger">The logger provided by the host.</param>
        public RtlTcpServer(ILogger<RtlTcpServer> logger)
        {
            // Store a reference to the logger
            _logger = logger;

            // Create the TCP listener worker thread
            _listenerThread = new(ListenerWorker)
            {
                Name = "TCPListenerThread"
            };
        }

        /// <inheritdoc/>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Log that the server is starting
            _logger.LogInformation("Starting TCP server on port 1234");

            // Set up the TCP listener on port 1234
            _listener = new(IPAddress.Any, 1234);

            // Start the TCP listener
            _listener.Start();

            // Start the listener worker thread
            _listenerThread.Start();

            // Log and return that the server has started
            _logger.LogInformation("TCP server is now running");
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            // Log that the server is stopping
            _logger.LogInformation("Stopping TCP server");

            // Indicate to the listener worker thread that it needs to stop, and stop the TCP listener
            _serverCancellationToken.Cancel();
            _listener?.Stop();

            // Wait until the listener thread stops
            await Task.Run(() => _listenerThread.Join(), cancellationToken);

            // Stop each of the running connections
            foreach (RtlTcpConnection connection in _connections)
            {
                await Task.Run(() => connection.Dispose());
            }

            // Log and return that the server has stopped
            _logger.LogInformation("TCP server has stopped");
        }
        #endregion

        #region Connection handling methods
        /// <summary>
        /// Worker for the listener thead. Continuously accepts and processes new connections until the server is stopped.
        /// </summary>
        private void ListenerWorker()
        {
            while (!_serverCancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Wait for a connection and accept it
                    TcpClient client = _listener!.AcceptTcpClient();

                    // Create a new connection instance to handle communication to the client, and add it to the list of connections
                    RtlTcpConnection connection = new(client);
                    connection.Disconnected += ClientDisconnected;
                    _connections.Add(connection);

                    // Log the connection
                    _logger.LogInformation($"Connected to {connection.ClientIP}");

                }
                catch (SocketException ex)
                {
                    if (ex.ErrorCode != (int)SocketError.Interrupted)
                    {
                        _logger.LogError(ex, "The TCP listener encountered an error");
                    }
                }
            }
        }

        /// <summary>
        /// Event handler for client disconnections. Disposes the connection and removes it from the list of connections.
        /// </summary>
        /// <param name="sender">The sending object.</param>
        /// <param name="e">The event arguments.</param>
        private void ClientDisconnected(object? sender, EventArgs e)
        {
            if (sender != null)
            {
                RtlTcpConnection connection = (RtlTcpConnection)sender;
                _connections.Remove(connection);
                connection.Dispose();

                // Log the disconnection
                _logger.LogInformation($"Disconnected from {connection.ClientIP}");
            }
        }
        #endregion
    }
}