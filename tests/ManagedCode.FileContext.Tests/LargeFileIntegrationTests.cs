using System.Text;

namespace ManagedCode.FileContext.Tests;

public sealed class LargeFileIntegrationTests
{
    private const long OneGibibyte = 1L * 1_024 * 1_024 * 1_024;
    private const int BlockLength = 4_096;
    private const int RangeByteLimit = 16 * 1_024;
    private const int RepeatedReadCount = 32;
    private const long MaximumAllocatedBytes = 16L * 1_024 * 1_024;
    private const string LargeFileName = "large-sparse.txt";

    [Fact]
    public async Task OneGiBSparseFile_WhenReadInRanges_RemainsBoundedAndRejectsUnboundedLine()
    {
        await using var scope = await TestStorageScope.CreateAsync();
        await CreateSparseFileAsync(Path.Combine(scope.Directory, LargeFileName));
        var options = new FileContextOptions { MaximumRangeReadBytes = RangeByteLimit };
        var store = new ManagedCodeStorageFileStore(scope.Storage, options);
        var service = new FileContextService(store, options);

        new FileInfo(Path.Combine(scope.Directory, LargeFileName)).Length.ShouldBe(OneGibibyte);
        await Should.ThrowAsync<IOException>(() => store.ReadAsync(LargeFileName));

        var first = await service.ReadRangeAsync(LargeFileName, startLine: 1, lineCount: 1);
        var second = await service.ReadRangeAsync(LargeFileName, startLine: 2, lineCount: 1);

        first.Content.ShouldStartWith("block-one");
        first.HasMore.ShouldBeTrue();
        second.Content.ShouldStartWith("block-two");
        second.HasMore.ShouldBeTrue();

        var allocatedBytesBeforeRepeatedReads = GC.GetTotalAllocatedBytes(precise: true);
        for (var index = 0; index < RepeatedReadCount; index++)
        {
            var range = await service.ReadRangeAsync(LargeFileName, startLine: 1, lineCount: 1);
            range.Content.ShouldStartWith("block-one");
        }

        var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBytesBeforeRepeatedReads;
        allocatedBytes.ShouldBeLessThan(MaximumAllocatedBytes);
        await Should.ThrowAsync<IOException>(() =>
            service.ReadRangeAsync(LargeFileName, startLine: 4, lineCount: 1));
    }

    private static async Task CreateSparseFileAsync(string path)
    {
        var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.Read,
            BlockLength,
            FileOptions.Asynchronous);
        await using (stream.ConfigureAwait(false))
        {
            await WriteSparseBlockAsync(stream, blockIndex: 0, "block-one").ConfigureAwait(false);
            await WriteSparseBlockAsync(stream, blockIndex: 1, "block-two").ConfigureAwait(false);
            await WriteSparseBlockAsync(stream, blockIndex: 2, "block-three").ConfigureAwait(false);
            stream.SetLength(OneGibibyte);
            await stream.FlushAsync().ConfigureAwait(false);
        }
    }

    private static async Task WriteSparseBlockAsync(FileStream stream, int blockIndex, string marker)
    {
        var blockStart = (long)blockIndex * BlockLength;
        stream.Position = blockStart;
        await stream.WriteAsync(Encoding.UTF8.GetBytes(marker)).ConfigureAwait(false);
        stream.Position = blockStart + BlockLength - 1;
        await stream.WriteAsync("\n"u8.ToArray()).ConfigureAwait(false);
    }
}
