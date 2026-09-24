using System;

namespace BeastCraft
{
    /// <summary>
    /// The game's diagnostic log. Engine-neutral: messages go to <see cref="Sink"/>, which defaults
    /// to standard error (warnings and errors are for developers, never player-facing UI). A host
    /// (the game shell, a test runner) may replace the sink to route them elsewhere.
    /// </summary>
    public static class Log
    {
        /// <summary>Receives every message with its level. Never null; assign null to restore the default.</summary>
        public static Action<LogLevel, string> Sink
        {
            get { return _sink; }
            set { _sink = value ?? WriteToStandardError; }
        }

        private static Action<LogLevel, string> _sink = WriteToStandardError;

        public static void Warning(string message)
        {
            _sink(LogLevel.Warning, message);
        }

        public static void Error(string message)
        {
            _sink(LogLevel.Error, message);
        }

        /// <summary>An error about a specific object (named in the message, as the Unity-era log did).</summary>
        public static void Error(string message, object context)
        {
            _sink(LogLevel.Error, message + (context == null ? string.Empty : " (context: " + context + ")"));
        }

        private static void WriteToStandardError(LogLevel level, string message)
        {
            Console.Error.WriteLine(message);
        }
    }

    /// <summary>Severity of a <see cref="Log"/> message.</summary>
    public enum LogLevel
    {
        Warning = 0,
        Error = 1
    }
}
