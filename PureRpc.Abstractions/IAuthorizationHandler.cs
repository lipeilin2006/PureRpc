using System.Threading;
using System.Threading.Tasks;

namespace PureRpc.Abstractions;

public interface IAuthorizationHandler
{
    ValueTask AuthorizeAsync(RpcContext context, string? policy, string? roles, CancellationToken ct);
}
