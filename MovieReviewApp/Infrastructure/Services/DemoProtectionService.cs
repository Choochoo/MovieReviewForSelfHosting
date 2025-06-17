using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Services;

public class DemoProtectionService
{
    private readonly InstanceManager _instanceManager;
    private readonly HashSet<string> _blockedOperations = new()
    {
        "Delete MovieSession",
        "Delete Person", 
        "Delete Award",
        "Purge audio files",
        "Reset database"
    };
    
    private const string DemoProtectionMessage = "Uh oh, don't mess with the demo data please! '{0}' is not allowed in demo mode.";
    
    public DemoProtectionService(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }
    
    public bool IsDemoInstance => _instanceManager.InstanceName?.Equals("demo", StringComparison.OrdinalIgnoreCase) == true;
    
    public void ValidateNotDemo(string operation = "operation")
    {
        if (IsDemoInstance && _blockedOperations.Contains(operation))
        {
            throw new DemoProtectionException(string.Format(DemoProtectionMessage, operation));
        }
    }
    
    public bool TryValidateNotDemo(string operation, out string errorMessage)
    {
        if (IsDemoInstance && _blockedOperations.Contains(operation))
        {
            errorMessage = string.Format(DemoProtectionMessage, operation);
            return false;
        }
        
        errorMessage = string.Empty;
        return true;
    }
}

public class DemoProtectionException : InvalidOperationException
{
    public DemoProtectionException(string message) : base(message) { }
}