using System.Threading;
using System.Threading.Tasks;

namespace Tromby.Security;

/// <summary>
/// Handles user consent storage and policy revalidation.
/// </summary>
public interface IConsentService
{
    /// <summary>
    /// Checks if the user has already provided consent matching current policy hash.
    /// </summary>
    Task<bool> HasConsentAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Stores user consent with the provided policy hash.
    /// </summary>
    Task StoreConsentAsync(string policyHash, CancellationToken cancellationToken);
}
