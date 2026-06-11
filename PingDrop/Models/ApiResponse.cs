namespace PingDrop.Models
{
    internal sealed record ApiResponse<T>
    {
        public bool IsSuccess { get; init; }
        public string? Message { get; init; }
        public T? Data { get; init; }
        public IReadOnlyList<string>? Errors { get; init; }

        /// <summary>Creates a successful response with data.</summary>
        public static ApiResponse<T> Success(T data, string? message = null)
            => new() { IsSuccess = true, Data = data, Message = message };

        /// <summary>Creates a successful response with data and a message formatted from parameters.</summary>
        public static ApiResponse<T> Success(T data, string messageFormat, params object[] args)
            => Success(data, string.Format(messageFormat, args));

        /// <summary>Creates a failure response with a message and optional error list.</summary>
        public static ApiResponse<T> Failure(string message, IEnumerable<string>? errors = null)
            => new() { IsSuccess = false, Message = message, Errors = errors?.ToList() };

        /// <summary>Creates a failure response with a message and one or more errors passed as params.</summary>
        public static ApiResponse<T> Failure(string message, params string[] errors)
            => Failure(message, errors as IEnumerable<string>);

        /// <summary>Creates a failure response from an exception, using its message and (if available) inner exceptions.</summary>
        public static ApiResponse<T> FromException(Exception ex, string? friendlyMessage = null)
        {
            var messages = new List<string>();
            var current = ex;
            while (current != null)
            {
                messages.Add(current.Message);
                current = current.InnerException;
            }

            return Failure(friendlyMessage ?? "An unexpected error occurred.", messages);
        }

        // ---------- Conditional ----------

        /// <summary>
        /// Creates a success response if <paramref name="condition"/> is true;
        /// otherwise returns a failure response with the provided error message.
        /// </summary>
        public static ApiResponse<T> FromCondition(bool condition, T data, string errorMessage)
            => condition ? Success(data) : Failure(errorMessage);
    }
}
