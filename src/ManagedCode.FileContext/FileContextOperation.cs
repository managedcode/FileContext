namespace ManagedCode.FileContext;

internal static class FileContextOperation
{
    public static async Task<T> RunAsync<T>(
        TimeSpan? timeout,
        Func<CancellationToken, Task<T>> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var deadline = timeout is { } duration ? new CancellationTokenSource(duration) : null;
        using var linked = deadline is null ? null : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        var operationToken = linked?.Token ?? cancellationToken;
        try
        {
            operationToken.ThrowIfCancellationRequested();
            var result = await operation(operationToken).ConfigureAwait(false);
            operationToken.ThrowIfCancellationRequested();
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
