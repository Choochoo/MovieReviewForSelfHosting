using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Infrastructure.Services;

public class DemoProtectionService
{
    private readonly InstanceManager _instanceManager;
    private readonly HashSet<string> _allowedInitialOperations = new()
    {
        "Upsert Setting", 
        "Insert Setting", 
        "Update Setting"
    };
    private bool _isInitialSetup = false;
    
    private const string DemoProtectionMessage = "Uh oh, don't mess with the demo data please! '{0}' is not allowed in demo mode.";
    
    public DemoProtectionService(InstanceManager instanceManager)
    {
        _instanceManager = instanceManager;
    }
    
    public bool IsDemoInstance => _instanceManager.InstanceName?.Equals("demo", StringComparison.OrdinalIgnoreCase) == true;
    
    public void ValidateNotDemo(string operation = "operation")
    {
        if (IsDemoInstance)
        {
            // Allow settings operations during initial setup
            if (_isInitialSetup && _allowedInitialOperations.Contains(operation))
            {
                return;
            }
            
            throw new DemoProtectionException(string.Format(DemoProtectionMessage, operation));
        }
    }
    
    public bool TryValidateNotDemo(string operation, out string errorMessage)
    {
        if (IsDemoInstance)
        {
            // Allow settings operations during initial setup
            if (_isInitialSetup && _allowedInitialOperations.Contains(operation))
            {
                errorMessage = string.Empty;
                return true;
            }
            
            errorMessage = string.Format(DemoProtectionMessage, operation);
            return false;
        }
        
        errorMessage = string.Empty;
        return true;
    }
    
    public void SetInitialSetupMode(bool isInitialSetup)
    {
        _isInitialSetup = isInitialSetup;
    }
    
    public IDisposable BeginInitialSetup()
    {
        return new InitialSetupScope(this);
    }
    
    private class InitialSetupScope : IDisposable
    {
        private readonly DemoProtectionService _service;
        
        public InitialSetupScope(DemoProtectionService service)
        {
            _service = service;
            _service.SetInitialSetupMode(true);
        }
        
        public void Dispose()
        {
            _service.SetInitialSetupMode(false);
        }
    }
}

public class DemoProtectionException : InvalidOperationException
{
    public DemoProtectionException(string message) : base(message) { }
}