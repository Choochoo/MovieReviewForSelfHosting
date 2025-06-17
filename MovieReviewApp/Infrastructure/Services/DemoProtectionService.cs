using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Services;

public class DemoProtectionService
{
    private readonly InstanceManager _instanceManager;
    
    public DemoProtectionService(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }
    
    public bool IsDemoInstance => _instanceManager.InstanceName?.Equals("demo", StringComparison.OrdinalIgnoreCase) == true;
    
    public void ValidateNotDemo(string operation = "operation")
    {
        if (IsDemoInstance)
        {
            throw new DemoProtectionException($"Uh oh, don't mess with the demo data please! '{operation}' is not allowed in demo mode.");
        }
    }
    
    public bool TryValidateNotDemo(string operation, out string errorMessage)
    {
        if (IsDemoInstance)
        {
            errorMessage = $"Uh oh, don't mess with the demo data please! '{operation}' is not allowed in demo mode.";
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