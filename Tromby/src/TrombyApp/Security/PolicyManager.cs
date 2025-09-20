using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace Tromby.Security;

/// <summary>
/// Provides access to policy configuration and ensures re-consent when policy changes.
/// </summary>
public sealed class PolicyManager
{
    private readonly IConfiguration _configuration;
    private readonly Lazy<string> _policyHash;

    /// <summary>
    /// Initializes a new instance of the <see cref="PolicyManager"/> class.
    /// </summary>
    public PolicyManager(IConfiguration configuration)
    {
        _configuration = configuration;
        _policyHash = new Lazy<string>(ComputePolicyHash, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>
    /// Gets the current policy hash fingerprint.
    /// </summary>
    public string CurrentPolicyHash => _policyHash.Value;

    /// <summary>
    /// Ensures the stored consent matches the current policy fingerprint.
    /// </summary>
    public async Task EnsurePolicyUpToDateAsync(IConsentService consentService, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!await consentService.HasConsentAsync(cancellationToken).ConfigureAwait(false))
        {
            throw new InvalidOperationException("Consent is required for the current policy hash.");
        }
    }

    private string ComputePolicyHash()
    {
        var section = _configuration.GetSection("policy");
        var builder = new StringBuilder();
        AppendSection(section, builder, string.Empty);
        using var sha = SHA256.Create();
        var computed = Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())));
        var declared = section["hash"];
        return string.IsNullOrWhiteSpace(declared) ? computed : $"{declared}:{computed}";
    }

    private static void AppendSection(IConfiguration section, StringBuilder builder, string prefix)
    {
        foreach (var child in section.GetChildren().OrderBy(child => child.Key, StringComparer.OrdinalIgnoreCase))
        {
            var key = string.IsNullOrEmpty(prefix) ? child.Key : $"{prefix}:{child.Key}";
            if (key.EndsWith(":hash", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (child.Value is not null)
            {
                builder.Append(key).Append('=').Append(child.Value).Append(';');
            }

            AppendSection(child, builder, key);
        }
    }
}
