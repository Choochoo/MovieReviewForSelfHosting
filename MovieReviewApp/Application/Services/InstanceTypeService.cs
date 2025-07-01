using MovieReviewApp.Infrastructure.Configuration;

namespace MovieReviewApp.Application.Services;

public class InstanceTypeService(InstanceManager instanceManager)
{
    public bool IsDemo()
    {
        string instanceName = instanceManager.InstanceName;
        return string.Equals(instanceName, "demo", StringComparison.OrdinalIgnoreCase);
    }

    public bool ShouldGenerateDemoData()
    {
        return IsDemo();
    }

    public string GetInstanceName()
    {
        return instanceManager.InstanceName;
    }
}