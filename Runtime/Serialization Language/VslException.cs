using System;

namespace Vapor.Serialization
{
    /// <summary>
    /// Thrown for VSL syntax errors, and for semantic mismatches when <see cref="VslOptions.Strict"/>
    /// is enabled.
    /// </summary>
    public class VslException : Exception
    {
        /// <summary>1-based line the error was detected on, or 0 when not positional.</summary>
        public int Line { get; }

        /// <summary>1-based column the error was detected at, or 0 when not positional.</summary>
        public int Column { get; }

        public VslException(string message) : base(message)
        {
        }

        public VslException(string message, Exception inner) : base(message, inner)
        {
        }

        public VslException(string message, int line, int column)
            : base($"{message} (line {line}, column {column})")
        {
            Line = line;
            Column = column;
        }
    }
}
