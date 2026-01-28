using MovieReviewApp.Infrastructure.Repositories;
using MovieReviewApp.Models;

namespace MovieReviewApp.Application.Services;

/// <summary>
/// Service responsible for managing voter identities.
/// Maps IP addresses to person names to prevent identity switching.
/// </summary>
public class VoterIdentityService(
    IRepository<VoterIdentity> repository,
    ILogger<VoterIdentityService> logger)
    : BaseService<VoterIdentity>(repository, logger)
{
    /// <summary>
    /// Gets the identity for a specific IP address
    /// </summary>
    public async Task<VoterIdentity?> GetIdentityByIpAsync(string ipAddress)
    {
        List<VoterIdentity> identities = await GetAllAsync();
        return identities
            .Where(i => i.IpAddress == ipAddress)
            .FirstOrDefault();
    }

    /// <summary>
    /// Gets the identity for a specific person name
    /// </summary>
    public async Task<VoterIdentity?> GetIdentityByNameAsync(string personName)
    {
        List<VoterIdentity> identities = await GetAllAsync();
        return identities
            .Where(i => i.PersonName.Equals(personName, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
    }

    /// <summary>
    /// Locks an IP address to a person's identity.
    /// Returns null if the IP is already locked to a different person.
    /// </summary>
    public async Task<VoterIdentity?> LockIdentityAsync(string ipAddress, string personName)
    {
        // Check if IP is already locked
        VoterIdentity? existingByIp = await GetIdentityByIpAsync(ipAddress);
        if (existingByIp != null)
        {
            // IP already locked - check if it's the same person
            if (existingByIp.PersonName.Equals(personName, StringComparison.OrdinalIgnoreCase))
                return existingByIp; // Same person, return existing

            // Different person - cannot switch
            _logger.LogWarning(
                "IP {IpAddress} tried to switch identity from {ExistingName} to {NewName}",
                ipAddress, existingByIp.PersonName, personName);
            return null;
        }

        // Check if person name is already locked to a different IP
        VoterIdentity? existingByName = await GetIdentityByNameAsync(personName);
        if (existingByName != null)
        {
            // Name already claimed by different IP
            _logger.LogWarning(
                "Person {PersonName} already claimed by IP {ExistingIp}, new IP {NewIp} attempting to claim",
                personName, existingByName.IpAddress, ipAddress);
            // Allow this - person may be on different device
            // Update the existing identity with new IP
            existingByName.IpAddress = ipAddress;
            existingByName.LockedAt = DateTime.UtcNow;
            return await UpdateAsync(existingByName);
        }

        // Create new identity lock
        VoterIdentity newIdentity = new VoterIdentity
        {
            IpAddress = ipAddress,
            PersonName = personName,
            LockedAt = DateTime.UtcNow
        };

        _logger.LogInformation(
            "Locked IP {IpAddress} to identity {PersonName}",
            ipAddress, personName);

        return await CreateAsync(newIdentity);
    }

    /// <summary>
    /// Checks if an IP address is locked to a specific person
    /// </summary>
    public async Task<bool> IsIpLockedToPersonAsync(string ipAddress, string personName)
    {
        VoterIdentity? identity = await GetIdentityByIpAsync(ipAddress);
        return identity != null &&
               identity.PersonName.Equals(personName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Checks if an IP address is locked to any person
    /// </summary>
    public async Task<bool> IsIpLockedAsync(string ipAddress)
    {
        VoterIdentity? identity = await GetIdentityByIpAsync(ipAddress);
        return identity != null;
    }
}
