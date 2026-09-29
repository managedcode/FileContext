using Microsoft.Extensions.Options;

namespace ManagedCode.FileContext;

internal sealed class FileContextOptionsValidator : IValidateOptions<FileContextOptions>
{
    public ValidateOptionsResult Validate(string? name, FileContextOptions options)
    {
        try
        {
            options.Validate();
            return ValidateOptionsResult.Success;
        }
        catch (InvalidOperationException exception)
        {
            return ValidateOptionsResult.Fail(exception.Message);
        }
    }
}
