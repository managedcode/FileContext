using ManagedCode.Communication;

namespace ManagedCode.FileContext;

internal static class StorageResult
{
    public static T GetValue<T>(Result<T> result, string operation)
    {
        if (result.IsFailed)
        {
            throw CreateException(operation, result.Problem.Detail ?? result.Problem.Title);
        }

        return result.Value;
    }

    public static void EnsureSuccess<T>(Result<T> result, string operation)
    {
        if (result.IsFailed)
        {
            throw CreateException(operation, result.Problem.Detail ?? result.Problem.Title);
        }
    }

    private static IOException CreateException(string operation, string? detail) =>
        new($"Storage operation '{operation}' failed: {detail ?? "unknown error"}.");
}
