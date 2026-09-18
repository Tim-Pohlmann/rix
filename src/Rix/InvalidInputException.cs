namespace Rix;

/// <summary>Thrown when caller-supplied input (a CLI flag, an environment variable, an API request
/// field) can't be turned into the strongly-typed value the rest of the program works with. It is
/// unrecoverable at the point it's raised — there is no valid instance to construct — so the
/// boundary that received the input (the CLI pipeline, the API server) is the one place that
/// catches it, reports <see cref="Exception.Message"/>, and stops. Everything in between stays
/// free of validation plumbing: types validate in their constructors and nothing else.</summary>
internal sealed class InvalidInputException : Exception
{
    public InvalidInputException(string message) : base(message) { }

    public InvalidInputException(string message, Exception inner) : base(message, inner) { }
}
