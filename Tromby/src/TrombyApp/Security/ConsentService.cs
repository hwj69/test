using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Windows.Security.Credentials;

namespace Tromby.Security;

/// <summary>
/// Persists consent via Windows Credential Locker and revalidates based on policy hash.
/// </summary>
public sealed class ConsentService : IConsentService
{
    private const string RESOURCE_NAME = "Tromby/Consent";
    private readonly PolicyManager _policyManager;
    private readonly ILogger<ConsentService> _logger;
    private readonly PasswordVault _vault = new();

    /// <summary>
    /// Initializes a new instance of the consent service.
    /// </summary>
    public ConsentService(PolicyManager policyManager, ILogger<ConsentService> logger)
    {
        _policyManager = policyManager;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<bool> HasConsentAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var credential = _vault.Retrieve(RESOURCE_NAME, Environment.UserName);
                credential.RetrievePassword();
                var storedHash = credential.Password;
                var currentHash = _policyManager.CurrentPolicyHash;
                var matches = string.Equals(storedHash, currentHash, StringComparison.Ordinal);
                if (!matches)
                {
                    _logger.LogInformation("Consent hash mismatch detected. Stored={Stored} Expected={Expected}.", storedHash, currentHash);
                }

                return matches;
            }
            catch (Exception ex)
            {
                _logger.LogInformation(ex, "Consent not present in credential locker.");
                return false;
            }
        }, cancellationToken);
    }

    /// <inheritdoc />
    public Task StoreConsentAsync(string policyHash, CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var hashToPersist = string.IsNullOrWhiteSpace(policyHash) ? _policyManager.CurrentPolicyHash : policyHash;
            try
            {
                var existing = _vault.Retrieve(RESOURCE_NAME, Environment.UserName);
                _vault.Remove(existing);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "No existing consent credential to remove.");
            }

            var credential = new PasswordCredential(RESOURCE_NAME, Environment.UserName, hashToPersist);
            _vault.Add(credential);
            _logger.LogInformation("Consent stored for hash {Hash}.", hashToPersist);
        }, cancellationToken);
    }
}
