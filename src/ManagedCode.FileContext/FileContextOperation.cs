namespace ManagedCode.FileContext;

internal static class FileContextOperation
{
    public static async Task<T> RunAsync<T>(
        TimeSpan? timeout,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken,
        Func<T, ValueTask>? releaseCancelledResult = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = timeout.HasValue ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken) : null;
        deadline?.CancelAfter(timeout.GetValueOrDefault());
        var operationToken = deadline?.Token ?? cancellationToken;
        try
        {
            operationToken.ThrowIfCancellationRequested();
            var result = await operation(operationToken).ConfigureAwait(false);
            if (operationToken.IsCancellationRequested)
            {
                if (releaseCancelledResult is not null)
                {
                    await releaseCancelledResult(result).ConfigureAwait(false);
                }
                operationToken.ThrowIfCancellationRequested();
            }
            return result;
        }
        catch (Exception exception) when (operationToken.IsCancellationRequested && exception is OperationCanceledException or IOException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"File-context operation exceeded the configured {timeout} timeout.", exception);
        }
    }

    public static Task RunAsync(
        TimeSpan? timeout,
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken)
    {
        return RunAsync(timeout, async token =>
        {
            await operation(token).ConfigureAwait(false);
            return true;
        }, cancellationToken);
    }
}
