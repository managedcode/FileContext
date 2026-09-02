using ManagedCode.Communication;

namespace ManagedCode.FileContext;

internal static class StorageResult
{
    public static T GetValue<T>(Result<T> result, string operation)
    {
        if (!result.IsSuccess || result.Value is null)
        {
            throw new IOException($"Storage operation '{operation}' failed: {result.Problem?.Detail ?? result.Problem?.Title ?? "unknown error"}.");
        }

        return result.Value;
    }

    public static void EnsureSuccess<T>(Result<T> result, string operation)
    {
        if (!result.IsSuccess)
        {
            throw new IOException($"Storage operation '{operation}' failed: {result.Problem?.Detail ?? result.Problem?.Title ?? "unknown error"}.");
        }
    }
}
