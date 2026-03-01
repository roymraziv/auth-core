namespace AuthCore.Core.Common;

public sealed class Result
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public string Error { get; }

    private Result(bool isSuccess, string? error = null)
    {
        IsSuccess = isSuccess;
        Error = error ?? string.Empty;
    }

    public static Result Success() => new(true);
    public static Result Failure(string error) => new(false, error ?? throw new ArgumentNullException(nameof(error)));
}

public sealed class Result<T>
{
    public bool IsSuccess { get; }
    public bool IsFailure => !IsSuccess;
    public T? Value { get; }
    public string Error { get; }

    private Result(bool isSuccess, T? value, string? error)
    {
        IsSuccess = isSuccess;
        Value = value;
        Error = error ?? string.Empty;
    }

    public static Result<T> Success(T value) => new(true, value, null);

    public static Result<T> Failure(string error) =>
        new(false, default, error ?? throw new ArgumentNullException(nameof(error)));
}