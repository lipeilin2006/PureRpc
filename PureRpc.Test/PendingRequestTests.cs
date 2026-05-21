using System.Buffers;
using System.Threading.Tasks.Sources;

namespace PureRpc.Test;

public sealed class PendingRequestTests
{
    [Fact]
    public void SetResult_ThenGetResult_ReturnsValue()
    {
        var pr = new PendingRequest();
        var data = new ReadOnlySequence<byte>(new byte[] { 1, 2, 3 });

        pr.SetResult(data);
        var result = pr.GetResult(pr.Version);

        Assert.True(result.ToArray().SequenceEqual(new byte[] { 1, 2, 3 }));
    }

    [Fact]
    public void SetException_ThenGetResult_Throws()
    {
        var pr = new PendingRequest();
        var ex = new InvalidOperationException("test error");

        pr.SetException(ex);
        var thrown = Assert.Throws<InvalidOperationException>(() => pr.GetResult(pr.Version));
        Assert.Equal("test error", thrown.Message);
    }

    [Fact]
    public void SetCanceled_ThenGetResult_ThrowsOperationCanceledException()
    {
        var pr = new PendingRequest();
        using var cts = new CancellationTokenSource();

        pr.SetCanceled(cts.Token);
        Assert.Throws<OperationCanceledException>(() => pr.GetResult(pr.Version));
    }

    [Fact]
    public void Version_IncrementsAfterSetResult()
    {
        var pr = new PendingRequest();
        var v1 = pr.Version;

        pr.SetResult(new ReadOnlySequence<byte>(Array.Empty<byte>()));
        pr.Reset();
        var v2 = pr.Version;

        Assert.NotEqual(v1, v2);
    }

    [Fact]
    public void Reset_AllowsReuse_SetResultAfterReset()
    {
        var pr = new PendingRequest();

        pr.SetResult(new ReadOnlySequence<byte>(new byte[] { 1 }));
        pr.GetResult(pr.Version);
        pr.Reset();

        pr.SetResult(new ReadOnlySequence<byte>(new byte[] { 2, 3 }));
        var result = pr.GetResult(pr.Version);

        Assert.True(result.ToArray().SequenceEqual(new byte[] { 2, 3 }));
    }

    [Fact]
    public void GetStatus_ReturnsPendingBeforeCompletion()
    {
        var pr = new PendingRequest();

        var status = pr.GetStatus(pr.Version);

        Assert.Equal(ValueTaskSourceStatus.Pending, status);
    }

    [Fact]
    public void GetStatus_ReturnsSucceededAfterSetResult()
    {
        var pr = new PendingRequest();
        pr.SetResult(new ReadOnlySequence<byte>(Array.Empty<byte>()));

        var status = pr.GetStatus(pr.Version);

        Assert.Equal(ValueTaskSourceStatus.Succeeded, status);
    }

    [Fact]
    public void AsValueTask_ReturnsValueTaskWithCorrectVersion()
    {
        var pr = new PendingRequest();
        var vt = pr.AsValueTask();

        Assert.Equal(pr.Version, vt.AsTask().AsyncState is PendingRequest p ? p.Version : 0);
    }

    [Fact]
    public void SetException_ThenGetStatus_ReturnsFaulted()
    {
        var pr = new PendingRequest();
        pr.SetException(new Exception("fail"));

        var status = pr.GetStatus(pr.Version);

        Assert.Equal(ValueTaskSourceStatus.Faulted, status);
    }

    [Fact]
    public void SetCanceled_ThenGetStatus_ReturnsCanceled()
    {
        var pr = new PendingRequest();
        pr.SetCanceled(CancellationToken.None);

        var status = pr.GetStatus(pr.Version);

        Assert.Equal(ValueTaskSourceStatus.Canceled, status);
    }
}
