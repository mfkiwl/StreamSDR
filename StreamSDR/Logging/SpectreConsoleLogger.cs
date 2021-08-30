using System;
using Microsoft.Extensions.Logging;
using Spectre.Console;

namespace StreamSDR.Logging
{
    /// <summary>
    /// Provides a logger that outputs to the console using the <see cref="N:Spectre.Console"/> library.
    /// </summary>
    public class SpectreConsoleLogger : ILogger
    {
        /// <summary>
        /// The name of the category the logger is for.
        /// </summary>
        private readonly string _categoryName;

        /// <summary>
        /// Initialises a new instance of the <see cref="SpectreConsoleLogger"/> class.
        /// </summary>
        /// <param name="categoryName">The name of the category the logger is for.</param>
        public SpectreConsoleLogger(string categoryName) => _categoryName = categoryName;

        /// <inheritdoc/>
        public IDisposable? BeginScope<TState>(TState state) => null;

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel) => true;

        /// <inheritdoc/>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            string levelText = string.Empty;
            string levelColour = string.Empty;

            // Set the log level text and colour
            switch (logLevel)
            {
                case LogLevel.Trace:
                    levelText = "Trace";
                    levelColour = "tan";
                    break;
                case LogLevel.Debug:
                    levelText = "Debug";
                    levelColour = "salmon1";
                    break;
                case LogLevel.Information:
                    levelText = "Info";
                    levelColour = "dodgerblue1";
                    break;
                case LogLevel.Warning:
                    levelText = "Warn";
                    levelColour = "darkorange";
                    break;
                case LogLevel.Error:
                    levelText = "Error";
                    levelColour = "red3_1";
                    break;
                case LogLevel.Critical:
                    levelText = "Crit";
                    levelColour = "purple";
                    break;
            }

            // Create the table to hold the time, level and message
            Table table = new();
            table.Border(TableBorder.None)
                 .HideHeaders()
                 .AddColumn("Time")
                 .AddColumn("Level")
                 .AddColumn("Message");

            // Add the rows to the table containing the information
            table.AddRow($"[grey]{DateTime.Now.ToString("HH:mm:ss zzz")}[/]", $"[[[bold {levelColour}]{levelText.PadRight(5)}[/]]]", $"[bold]{_categoryName}:[/]");
            table.AddRow(string.Empty, string.Empty, formatter(state, exception));
            if (exception != null)
            {
                ExceptionFormats exceptionFormat = ExceptionFormats.ShortenPaths | ExceptionFormats.ShortenTypes | ExceptionFormats.ShortenMethods;
                table.AddRow(new Text(string.Empty), new Text(string.Empty), exception.GetRenderable(exceptionFormat));
            }

            // Render the outer table to the console
            AnsiConsole.Render(table);
        }
    }
}